using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.OS;

namespace TellMe.App.Platforms.Android;

public class BleDiscoveryManager{

    private readonly Dictionary<string, DateTime> _lastSyncTimes = new();
    private readonly TimeSpan _syncCooldown = TimeSpan.FromMinutes(1);


    private static readonly ParcelUuid TellMeServiceUuid = ParcelUuid.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01123")!;
    private readonly BluetoothAdapter? _adapter;
    private BluetoothLeAdvertiser? _advertiser;
    private BluetoothLeScanner? _scanner;

    public event Action<BluetoothDevice>? PeerDiscovered;
    public event Action<string>? StatusLog;

    public BleDiscoveryManager(){
        _adapter = BluetoothAdapter.DefaultAdapter;
    }

    private bool CanSyncWith(string address)
    {
        // Si l'appareil n'a JAMAIS été vu, on peut synchroniser tout de suite !
        if (!_lastSyncTimes.TryGetValue(address, out var lastTime))
        {
            return true;
        }

        // Sinon, on vérifie si plus d'une minute s'est écoulée
        return (DateTime.UtcNow - lastTime) > _syncCooldown;
    }


    public void StartAdvertising()
    {

        if (_adapter == null || !_adapter.IsEnabled) return;
        _advertiser = _adapter.BluetoothLeAdvertiser;
        if (_advertiser == null) return;

        var settings = new AdvertiseSettings.Builder()
        .SetAdvertiseMode(AdvertiseMode.LowPower)// Économie de batterie
        .SetTxPowerLevel(AdvertiseTx.PowerMedium) // Portée moyenne (~10m)
        .SetConnectable(true)
        .Build();

        var data = new AdvertiseData.Builder()
        .AddServiceUuid(TellMeServiceUuid)
        .SetIncludeDeviceName(false)
        .Build();

        var callback = new TellMeAdvertiseCallback(msg => StatusLog?.Invoke(msg));
        _advertiser.StartAdvertising(settings,data,callback);

    }   
    public void StartScanning(){

        if (_adapter == null || !_adapter.IsEnabled) return;
        _scanner = _adapter.BluetoothLeScanner;
        if (_scanner == null) return;

        var filter = new ScanFilter.Builder()
        .SetServiceUuid(TellMeServiceUuid).Build();
        var filters = new List<ScanFilter>{filter};
        var settings = new ScanSettings.Builder()
        .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowPower).Build();

           var callback = new TellMeScanCallback(
        device => {
        if (CanSyncWith(device.Address ?? ""))
        {
            _lastSyncTimes[device.Address ?? ""] = DateTime.UtcNow;
            PeerDiscovered?.Invoke(device);
        }
        },
        msg => StatusLog?.Invoke(msg)
        );
        _scanner.StartScan(filters, settings, callback);
        StatusLog?.Invoke("Radar BLE démarré : En attente de pairs...");
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

        private readonly Action<BluetoothDevice> _onPeerDiscovered;
        private readonly Action<string> _logger;
        public TellMeScanCallback(Action<BluetoothDevice> onPeerDiscovered, Action<string> logger){
            _onPeerDiscovered = onPeerDiscovered;
            _logger = logger;
        }

        public override void OnScanResult(ScanCallbackType callbacktype, ScanResult? result)
        {
            if(result?.Device != null){
                _logger($"Radar BLE: Pair detecte({result.Device.Address})!");
                _onPeerDiscovered(result.Device);
            }
        }

        public override void OnScanFailed(ScanFailure errorcode){
            _logger($"Echec du scan BLE : Erreur {errorcode}");
        }
    }

}

