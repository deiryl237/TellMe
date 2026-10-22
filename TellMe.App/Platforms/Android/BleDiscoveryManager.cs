using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;

namespace TellMe.App.Platforms.Android;

public class DiscoveredNeighbor
{
    public string Address { get; }
    public int Rssi { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime LastSyncAttempt { get; set; } = DateTime.MinValue;
    public byte[]? AccountHash { get; set; }
    public string? DeviceName { get; set; }
    // Objet BluetoothDevice issu du scan BLE (conserve le type d'adresse : publique ou aléatoire/RPA)
    public BluetoothDevice? Device { get; set; }

    public DiscoveredNeighbor(string address, int rssi, byte[]? accountHash = null, string? deviceName = null, BluetoothDevice? device = null)
    {
        Address = address;
        Rssi = rssi;
        LastSeen = DateTime.UtcNow;
        AccountHash = accountHash;
        DeviceName = deviceName;
        Device = device;
    }
}

public class BleDiscoveryManager{

    private readonly Dictionary<string, DiscoveredNeighbor> _neighbors = new();
    private readonly object _neighborLock = new();

    private static readonly ParcelUuid TellMeServiceUuid = ParcelUuid.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01123")!;
    private readonly BluetoothAdapter? _adapter;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothLeScanner? _scanner;
    private TellMeAdvertiseCallback? _advertiseCallback;
    private TellMeScanCallback? _scanCallback;
    private byte[]? _myAccountHash;

    private string? _priorityTargetAddress;
    private string? _priorityTargetAccountId;
    private DateTime _priorityDeadline = DateTime.MinValue;
    private DateTime _lastScanStart = DateTime.MinValue;

    public bool IsPriorityMode => DateTime.UtcNow < _priorityDeadline;

    public event Action<string>? StatusLog;

    public BleDiscoveryManager(){
        _adapter = BluetoothAdapter.DefaultAdapter;
    }

    public void SetMyAccountHash(byte[]? hash)
    {
        _myAccountHash = hash;
        PulseRadar();
    }

    public void SetPriorityTarget(string? address, string? accountId, int durationSeconds = 15)
    {
        _priorityTargetAddress = address;
        _priorityTargetAccountId = accountId;
        _priorityDeadline = DateTime.UtcNow.AddSeconds(durationSeconds);
        ResetCooldown();
    }

    public void ClearPriorityTarget()
    {
        _priorityDeadline = DateTime.MinValue;
        _priorityTargetAddress = null;
        _priorityTargetAccountId = null;
    }

    public string? ResolveTargetAddress()
    {
        lock (_neighborLock)
        {
            // 1. Si on a l'ID de compte cible, calculer son hash de 4 octets et matcher avec le phare BLE
            if (!string.IsNullOrEmpty(_priorityTargetAccountId))
            {
                try
                {
                    byte[] targetHash = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(_priorityTargetAccountId)
                    ).Take(4).ToArray();

                    var match = _neighbors.Values.FirstOrDefault(n => 
                        n.AccountHash != null && n.AccountHash.SequenceEqual(targetHash)
                    );
                    if (match != null) return match.Address;
                }
                catch { }
            }

            // 2. Si une adresse MAC valide a été fournie directement
            if (!string.IsNullOrEmpty(_priorityTargetAddress) && 
                System.Text.RegularExpressions.Regex.IsMatch(_priorityTargetAddress, "^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$"))
            {
                return _priorityTargetAddress;
            }

            // 3. Si l'adresse passée est en fait un nom d'appareil Bluetooth (adapter.Name)
            if (!string.IsNullOrEmpty(_priorityTargetAddress))
            {
                var matchByName = _neighbors.Values.FirstOrDefault(n => 
                    !string.IsNullOrEmpty(n.DeviceName) && 
                    n.DeviceName.Equals(_priorityTargetAddress, StringComparison.OrdinalIgnoreCase)
                );
                if (matchByName != null) return matchByName.Address;
            }

            // 4. Repli sur le voisin au RSSI le plus puissant (le téléphone tenu à 20cm devant la caméra)
            var closest = _neighbors.Values
                .Where(n => (DateTime.UtcNow - n.LastSeen).TotalSeconds <= 15)
                .OrderByDescending(n => n.Rssi)
                .FirstOrDefault();

            if (closest != null) return closest.Address;

            return null; // Ne jamais renvoyer un nom non-MAC
        }
    }

    public void OnPeerSeen(string address, int rssi, byte[]? accountHash = null, string? deviceName = null, BluetoothDevice? device = null)
    {
        if (string.IsNullOrEmpty(address)) return;

        lock (_neighborLock)
        {
            // Purge immédiate de toute ancienne adresse MAC fantôme/RPA pour le même compte TellMe !
            if (accountHash != null && accountHash.Length >= 4)
            {
                var obsoleteKeys = _neighbors
                    .Where(kv => !kv.Key.Equals(address, StringComparison.OrdinalIgnoreCase) &&
                                 kv.Value.AccountHash != null &&
                                 kv.Value.AccountHash.SequenceEqual(accountHash))
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var oldKey in obsoleteKeys)
                {
                    _neighbors.Remove(oldKey);
                }
            }

            if (_neighbors.TryGetValue(address, out var existing))
            {
                existing.Rssi = rssi;
                existing.LastSeen = DateTime.UtcNow;
                if (accountHash != null && accountHash.Length > 0) existing.AccountHash = accountHash;
                if (!string.IsNullOrEmpty(deviceName)) existing.DeviceName = deviceName;
                if (device != null) existing.Device = device;
            }
            else
            {
                _neighbors[address] = new DiscoveredNeighbor(address, rssi, accountHash, deviceName, device);
            }
        }
    }

    /// <summary>
    /// Renvoie l'objet BluetoothDevice mémorisé lors du scan BLE pour cette adresse (null si inconnu).
    /// </summary>
    public BluetoothDevice? GetDevice(string address)
    {
        if (string.IsNullOrEmpty(address)) return null;
        lock (_neighborLock)
        {
            return _neighbors.TryGetValue(address, out var n) ? n.Device : null;
        }
    }

    /// <summary>
    /// Récupère la liste des voisins actifs (les plus proches physiquement selon RSSI)
    /// et évince les voisins inactifs depuis plus de 15 secondes.
    /// </summary>
    public List<DiscoveredNeighbor> GetActiveNeighbors(int maxCount = 3)
    {
        lock (_neighborLock)
        {
            var now = DateTime.UtcNow;
            var staleKeys = _neighbors
                .Where(kv => (now - kv.Value.LastSeen).TotalSeconds > 4)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _neighbors.Remove(key);
            }

            // Trier par proximité : RSSI le plus fort d'abord
            return _neighbors.Values
                .OrderByDescending(n => n.Rssi)
                .Take(maxCount)
                .ToList();
        }
    }

    /// <summary>
    /// Sélectionne le prochain voisin du Top-3 à synchroniser en Round-Robin.
    /// Évite de harceler un voisin qui vient tout juste d'être tenté (cooldown de 3s par voisin).
    /// Si un mode prioritaire est actif, renvoie toujours la cible prioritaire.
    /// </summary>
    public string? GetNextTargetAddress()
    {
        if (IsPriorityMode)
        {
            var priorityAddr = ResolveTargetAddress();
            if (!string.IsNullOrEmpty(priorityAddr))
            {
                return priorityAddr;
            }
        }

        var topNeighbors = GetActiveNeighbors(maxCount: 3);
        if (topNeighbors.Count == 0) return null;

        var now = DateTime.UtcNow;
        var candidates = topNeighbors
            .Where(n => (now - n.LastSyncAttempt).TotalMilliseconds >= 350)
            .OrderBy(n => n.LastSyncAttempt)
            .ToList();

        if (candidates.Count == 0) return null;

        var selected = candidates[0];
        selected.LastSyncAttempt = now;
        return selected.Address;
    }

    public void ResetCooldown(){
        lock (_neighborLock)
        {
            foreach (var n in _neighbors.Values)
            {
                n.LastSyncAttempt = DateTime.MinValue;
            }
        }
    }

    public void PulseRadar()
    {
        try
        {
            StopAdvertising();
            StartAdvertising();
        }
        catch { }
    }

    public void MarkSynced(string address)
    {
        if (string.IsNullOrEmpty(address)) return;
        lock (_neighborLock)
        {
            if (_neighbors.TryGetValue(address, out var n))
            {
                n.LastSyncAttempt = DateTime.UtcNow;
            }
        }
    }

    public void MarkAttemptFailed(string address)
    {
        if (string.IsNullOrEmpty(address)) return;
        lock (_neighborLock)
        {
            if (_neighbors.TryGetValue(address, out var n))
            {
                // En cas d'échec / collision, petit recul de 150ms pour laisser la priorité à l'autre et retenter dès 200ms
                n.LastSyncAttempt = DateTime.UtcNow - TimeSpan.FromMilliseconds(150);
            }
        }
    }

    public void EnsureRunning()
    {
        if (_adapter == null || !_adapter.IsEnabled) return;

        if (_advertiseCallback == null)
        {
            StartAdvertising();
        }

        if (_scanCallback == null)
        {
            StartScanning();
        }
    }

    public void OnBluetoothDisabled()
    {
        StopAdvertising();
        StopScanning();
        lock (_neighborLock)
        {
            _neighbors.Clear();
        }
    }

    public void OnBluetoothEnabled()
    {
        StartAdvertising();
        StartScanning();
    }

    public void StartAdvertising()
    {
        try
        {
            if (_adapter == null || !_adapter.IsEnabled)
            {
                StatusLog?.Invoke("Phare BLE : Bluetooth désactivé.");
                return;
            }
            _advertiser = _adapter.BluetoothLeAdvertiser;
            if (_advertiser == null)
            {
                StatusLog?.Invoke("Phare BLE : non supporté ou permission manquante.");
                return;
            }

            // Arrêter l'ancien phare avant d'en allumer un nouveau pour éviter TooManyAdvertisers
            StopAdvertising();

            var settings = new AdvertiseSettings.Builder()
                .SetAdvertiseMode(AdvertiseMode.LowLatency)
                .SetTxPowerLevel(AdvertiseTx.PowerHigh)
                .SetConnectable(true)
                .Build();

            var dataBuilder = new AdvertiseData.Builder()
                .AddServiceUuid(TellMeServiceUuid)
                .SetIncludeDeviceName(false);

            if (_myAccountHash != null && _myAccountHash.Length > 0)
            {
                dataBuilder.AddManufacturerData(0xFFFF, _myAccountHash);
            }

            var data = dataBuilder.Build();

            var scanResponseBuilder = new AdvertiseData.Builder()
                .SetIncludeDeviceName(true);
            var scanResponse = scanResponseBuilder.Build();

            _advertiseCallback = new TellMeAdvertiseCallback(msg => StatusLog?.Invoke(msg));
            _advertiser.StartAdvertising(settings, data, scanResponse, _advertiseCallback);
        }
        catch (Exception ex)
        {
            StatusLog?.Invoke($"Phare BLE erreur : {ex.Message}");
        }
    }

    public void StopAdvertising()
    {
        try
        {
            if (_advertiser != null && _advertiseCallback != null)
            {
                _advertiser.StopAdvertising(_advertiseCallback);
            }
        }
        catch { }
        finally
        {
            _advertiseCallback = null;
        }
    }

    public void StartScanning()
    {
        try
        {
            if (_adapter == null || !_adapter.IsEnabled)
            {
                StatusLog?.Invoke("Radar BLE : Bluetooth désactivé.");
                return;
            }
            _scanner = _adapter.BluetoothLeScanner;
            if (_scanner == null)
            {
                StatusLog?.Invoke("Radar BLE : scanner non disponible.");
                return;
            }

            StopScanning();

            // Filtrage logiciel : on démarre le scan sans filtre matériel car les puces
            // MediaTek et Snapdragon ignorent ou rejettent fréquemment les filtres matériels 128-bit.
            var filters = new List<ScanFilter>();
            var settings = new ScanSettings.Builder()
                .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)
                .SetReportDelay(0)
                .Build();

            _scanCallback = new TellMeScanCallback(
                this,
                msg => StatusLog?.Invoke(msg)
            );
            _scanner.StartScan(filters, settings, _scanCallback);
            _lastScanStart = DateTime.UtcNow;
            StatusLog?.Invoke("Radar BLE démarré : En attente de pairs...");
        }
        catch (Exception ex)
        {
            StatusLog?.Invoke($"Radar BLE erreur : {ex.Message}");
        }
    }

    public void StopScanning()
    {
        try
        {
            if (_scanner != null && _scanCallback != null)
            {
                _scanner.StopScan(_scanCallback);
            }
        }
        catch { }
        finally
        {
            _scanCallback = null;
        }
    }


    

    private class TellMeAdvertiseCallback : AdvertiseCallback{

    private readonly Action<string> _logger;
    public  TellMeAdvertiseCallback (Action<string> logger) => _logger = logger;

    public override void OnStartSuccess(AdvertiseSettings? settingsInEffect){
        _logger("Phare BLE allumé: TellMe diffuse sa presence.");
    }
    public override void OnStartFailure(AdvertiseFailure errorcode){
        _logger($"Echec du phare BLE : Erreur {errorcode}");
    }

    
    }
    private class TellMeScanCallback : ScanCallback {

        private readonly BleDiscoveryManager _manager;
        private readonly Action<string> _logger;
        public TellMeScanCallback(BleDiscoveryManager manager, Action<string> logger){
            _manager = manager;
            _logger = logger;
        }

        public override void OnScanResult(ScanCallbackType callbacktype, ScanResult? result)
        {
            if (result?.Device == null) return;

            // Filtrage logiciel par UUID TellMe
            var serviceUuids = result.ScanRecord?.ServiceUuids;
            bool isTellMe = serviceUuids != null && (
                serviceUuids.Contains(TellMeServiceUuid) ||
                serviceUuids.Any(u => u.Uuid != null && u.Uuid.ToString().Equals(TellMeServiceUuid.Uuid.ToString(), StringComparison.OrdinalIgnoreCase))
            );

            if (isTellMe && !string.IsNullOrEmpty(result.Device.Address))
            {
                byte[]? accountHash = result.ScanRecord?.GetManufacturerSpecificData(0xFFFF);
                string? deviceName = result.Device.Name ?? result.ScanRecord?.DeviceName;
                _manager.OnPeerSeen(result.Device.Address, result.Rssi, accountHash, deviceName, result.Device);
            }
        }

        public override void OnScanFailed(ScanFailure errorcode){
            _logger($"Echec du scan BLE : Erreur {errorcode}");
            if (errorcode == ScanFailure.ApplicationRegistrationFailed)
            {
                // Dans Android, cette erreur survient lors d'un redémarrage rapide.
                // Une réactivation après 1.5s résout automatiquement le slot Bluetooth.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1500);
                    _manager.StartScanning();
                });
            }
        }
    }

}

