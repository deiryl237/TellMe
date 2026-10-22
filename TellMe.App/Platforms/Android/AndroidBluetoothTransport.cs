using Android.Bluetooth;
using Java.Util;
using TellMe.Core.Network;

namespace TellMe.App.Platforms.Android;

/// <summary>
/// Transport TellMe 100% BLE (GATT) : aucun appairage Bluetooth n'est nécessaire.
///
/// - Pigeonnier (serveur GATT) : expose le service TellMe avec 2 caractéristiques :
///     RX (Write)  : le pair connecté nous écrit ses octets
///     TX (Notify) : on pousse nos octets au pair connecté
/// - Pigeon voyageur (client GATT) : se connecte au phare BLE d'un voisin, s'abonne à TX et écrit dans RX.
///
/// L'architecture (radar, Top-3 Round-Robin, Flash Sync, mode prioritaire, timers de session)
/// est strictement identique à l'ancienne version RFCOMM : seul le "tuyau" change.
/// </summary>
public class AndroidBluetoothTransport : ITransport
{
    // L'UUID unique qui identifie le service TellMe dans l'air Bluetooth (identique au phare BLE)
    private static readonly UUID TellMeUuid = UUID.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01123")!;
    private static readonly UUID RxCharUuid = UUID.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01124")!;
    private static readonly UUID TxCharUuid = UUID.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01125")!;
    private static readonly UUID CccdUuid = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;
    private static readonly byte[] EnableNotificationValue = { 0x01, 0x00 };

    private const int DesiredMtu = 512;
    private const int DefaultMtu = 23;
    private const int ConnectTimeoutMs = 1500;
    private const int ChunkTimeoutMs = 2000;

    private readonly BleDiscoveryManager _bleManager;
    private readonly BluetoothAdapter? _adapter;
    private CancellationTokenSource? _cts;
    private bool _isConnecting = false;
    private System.Timers.Timer? _sessionTimer;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _loopSignal = new(0, 1);

    public void TriggerImmediateLoop()
    {
        if (_loopSignal.CurrentCount == 0)
        {
            try { _loopSignal.Release(); } catch { }
        }
    }

    // --- Pigeonnier (serveur GATT) ---
    private BluetoothGattServer? _gattServer;
    private BluetoothGattCharacteristic? _txChar;
    private readonly Dictionary<string, int> _serverMtus = new(StringComparer.OrdinalIgnoreCase);

    // --- Session active (un seul pair à la fois, comme avec RFCOMM) ---
    private enum SessionRole { None, Client, Server }
    private readonly object _sessionLock = new();
    private SessionRole _role = SessionRole.None;
    private BluetoothGatt? _clientGatt;
    private BluetoothGattCharacteristic? _clientRxChar;
    private BluetoothDevice? _serverPeer;
    private string? _peerAddress;
    private int _sessionMtu = DefaultMtu;
    private TaskCompletionSource<bool>? _pendingChunk;

    public bool IsConnected => _role != SessionRole.None;
    public string? ConnectedPeerAddress => _peerAddress;
    private volatile bool _isListeningPriority = false;

    public event Action<byte[]>? DataReceived;
    public event Action? PeerConnected;

    public event Action<string>? StatusChanged; // Pour afficher ce qui se passe sur l'écran

    public void SetListeningPriority(bool isPriority)
    {
        _isListeningPriority = isPriority;
        if (isPriority)
        {
            NotifyStatus("Mode récepteur ami actif (pigeonnier en écoute prioritaire)...");
            _bleManager.PulseRadar();
        }
    }

    public void SetMyAccountHash(byte[]? hash)
    {
        _bleManager.SetMyAccountHash(hash);
    }

    public async Task PrioritizeTargetAsync(string targetAddress, string? targetAccountId = null)
    {
        _bleManager.SetPriorityTarget(targetAddress, targetAccountId, durationSeconds: 25);
        NotifyStatus($"Mode prioritaire ami activé pour {targetAddress} (recherche directe)...");

        // Lancer la boucle de persistance rapide en tâche de fond (Retry Loop)
        _ = Task.Run(async () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline)
            {
                if (IsConnected)
                {
                    // Si on est déjà connecté à la cible, parfait !
                    string? current = ConnectedPeerAddress;
                    string? resolved = _bleManager.ResolveTargetAddress();
                    if (!string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(resolved) &&
                        current.Equals(resolved, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    // Si on est connecté à un tiers (ex: Xiaomi), on attend proprement que sa session Flash Sync (500ms) se termine
                    await Task.Delay(350);
                    continue;
                }

                if (_isConnecting)
                {
                    await Task.Delay(300);
                    continue;
                }

                var targetMac = _bleManager.ResolveTargetAddress();
                if (!string.IsNullOrEmpty(targetMac))
                {
                    var peer = ResolveDevice(targetMac);
                    if (peer != null)
                    {
                        NotifyStatus($"Tentative de connexion prioritaire vers {targetMac}...");
                        await ConnectToDeviceAsync(peer);
                        if (IsConnected)
                        {
                            break;
                        }
                    }
                }

                // Si la cible est occupée (en train de finir 500ms avec un autre téléphone), on attend 350ms et on réessaie !
                await Task.Delay(350);
            }
        });
        await Task.CompletedTask;
    }

    private void NotifyStatus(string message)
    {
        try { global::Android.Util.Log.Info("TellMe", message); } catch { }
        StatusChanged?.Invoke(message);
    }

    /// <summary>
    /// Récupère l'objet BluetoothDevice du scan BLE (qui connaît le type d'adresse aléatoire/RPA),
    /// sinon le reconstruit à partir de l'adresse.
    /// </summary>
    private BluetoothDevice? ResolveDevice(string address)
    {
        var device = _bleManager.GetDevice(address);
        if (device != null) return device;
        try { return _adapter?.GetRemoteDevice(address); } catch { return null; }
    }

    public void ResetCooldown()
    {
        _bleManager.ResetCooldown();
    }

    public void OnSyncCompleted()
    {
        if (!IsConnected) return;
        NotifyStatus("Flash Sync terminée. Rotation vers pair suivant...");
        EndSession("Connexion fermée avec le pair.");
        TriggerImmediateLoop();
    }

    private void ResetSessionTimer(double timeoutMs = 6000)
    {
        try
        {
            _sessionTimer?.Stop();
            _sessionTimer?.Dispose();
            _sessionTimer = new System.Timers.Timer(timeoutMs) { AutoReset = false };
            _sessionTimer.Elapsed += (s, e) =>
            {
                if (!IsConnected) return;
                NotifyStatus("Session terminée (timeout). Prêt pour pair suivant.");
                EndSession("Connexion fermée avec le pair.");
            };
            _sessionTimer.Start();
        }
        catch { }
    }

    private void StopSessionTimer()
    {
        try { _sessionTimer?.Stop(); _sessionTimer?.Dispose(); _sessionTimer = null; } catch { }
    }

    // ============================================================
    //  Gestion de session (équivalent de l'ouverture/fermeture du socket RFCOMM)
    // ============================================================

    /// <summary>
    /// Ferme la session courante (client ou serveur) et libère le canal pour le pair suivant.
    /// </summary>
    private void EndSession(string? reason)
    {
        SessionRole role;
        BluetoothGatt? gatt;
        BluetoothDevice? serverPeer;
        TaskCompletionSource<bool>? pending;

        lock (_sessionLock)
        {
            role = _role;
            if (role == SessionRole.None) return;
            gatt = _clientGatt;
            serverPeer = _serverPeer;
            pending = _pendingChunk;

            _role = SessionRole.None;
            _clientGatt = null;
            _clientRxChar = null;
            _serverPeer = null;
            _peerAddress = null;
            _sessionMtu = DefaultMtu;
            _pendingChunk = null;
        }

        StopSessionTimer();
        pending?.TrySetResult(false);

        if (role == SessionRole.Client && gatt != null)
        {
            SafeCloseGatt(gatt);
        }
        else if (role == SessionRole.Server && serverPeer != null)
        {
            try { _gattServer?.CancelConnection(serverPeer); } catch { }
        }

        if (reason != null) NotifyStatus(reason);
        NotifyStatus("Pigeonnier ouvert : En attente de voisins...");
        TriggerImmediateLoop();
    }

    private static void SafeCloseGatt(BluetoothGatt? gatt)
    {
        if (gatt == null) return;
        try { gatt.Disconnect(); } catch { }
        try { gatt.Close(); } catch { }
    }

    /// <summary>
    /// Livre des octets reçus à la couche SyncEngine (le framing 4 octets gère la fragmentation BLE).
    /// </summary>
    private void DeliverIncoming(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        ResetSessionTimer(12000);
        NotifyStatus($"Paquet reçu ({data.Length} octets) !");
        DataReceived?.Invoke(data);
    }

    private BluetoothStateReceiver? _stateReceiver;

    private void RegisterBluetoothStateReceiver()
    {
        if (_stateReceiver != null) return;
        try
        {
            var context = global::Android.App.Application.Context;
            _stateReceiver = new BluetoothStateReceiver(this);
            var filter = new global::Android.Content.IntentFilter(BluetoothAdapter.ActionStateChanged);
            context.RegisterReceiver(_stateReceiver, filter);
        }
        catch { }
    }

    public void OnBluetoothStateChanged(bool isEnabled)
    {
        if (isEnabled)
        {
            NotifyStatus("Bluetooth activé : Reprise du radar et des connexions...");
            OpenGattServer();
            _bleManager.OnBluetoothEnabled();
        }
        else
        {
            NotifyStatus("Bluetooth désactivé ou indisponible.");
            _bleManager.OnBluetoothDisabled();
            EndSession(null);
            CloseGattServer();
        }
    }

    public AndroidBluetoothTransport()
    {
        _adapter = BluetoothAdapter.DefaultAdapter;
        _bleManager = new BleDiscoveryManager();
        _bleManager.StatusLog += msg => NotifyStatus(msg);
    }

    /// <summary>
    /// Démarre le pigeonnier (serveur GATT), le phare, le radar et la boucle de rotation.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            TellMeBackgroundService.Start(global::Android.App.Application.Context);
        }
        catch (Exception ex)
        {
            NotifyStatus($"Service d'arrière-plan : {ex.Message}");
        }

        RegisterBluetoothStateReceiver();

        await StopAsync();

        if (_adapter != null && _adapter.IsEnabled)
        {
            OpenGattServer();                // Ouvre le Pigeonnier (avant le phare, pour être joignable immédiatement)
            _bleManager.StartAdvertising();  // Allume le Phare
            _bleManager.StartScanning();     // Allume le Radar
        }
        else
        {
            NotifyStatus("Bluetooth désactivé ou indisponible.");
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => NeighborRotationLoop(_cts.Token));
    }

    /// <summary>
    /// Boucle de rotation Round-Robin : sélectionne à tour de rôle les voisins du Top-3
    /// détectés par le radar BLE pour des synchronisations flash ultra-rapides.
    /// </summary>
    private async Task NeighborRotationLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Réveil instantané dès qu'une session se termine (0 ms d'attente), sinon battement régulier de 120ms
                await _loopSignal.WaitAsync(120, ct);

                if (_adapter == null || !_adapter.IsEnabled)
                {
                    continue;
                }

                // S'assurer que le radar, le phare BLE et le pigeonnier tournent dès que Bluetooth est activé
                _bleManager.EnsureRunning();
                if (_gattServer == null) OpenGattServer();

                if (IsConnected || _isConnecting)
                {
                    continue;
                }

                var nextAddress = _bleManager.GetNextTargetAddress();
                if (!string.IsNullOrEmpty(nextAddress))
                {
                    var nextPeer = ResolveDevice(nextAddress);
                    if (nextPeer != null)
                    {
                        await ConnectToDeviceAsync(nextPeer);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    // ============================================================
    //  PIGEONNIER : serveur GATT (remplace ListenUsingInsecureRfcomm + Accept)
    // ============================================================

    private void OpenGattServer()
    {
        if (_gattServer != null) return;
        try
        {
            if (_adapter == null || !_adapter.IsEnabled) return;
            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(global::Android.Content.Context.BluetoothService) as BluetoothManager;
            if (manager == null)
            {
                NotifyStatus("Pigeonnier BLE : BluetoothManager indisponible.");
                return;
            }

            var server = manager.OpenGattServer(context, new TellMeGattServerCallback(this));
            if (server == null)
            {
                NotifyStatus("Pigeonnier BLE : impossible d'ouvrir le serveur GATT.");
                return;
            }

            var service = new BluetoothGattService(TellMeUuid, GattServiceType.Primary);

            var rx = new BluetoothGattCharacteristic(
                RxCharUuid,
                GattProperty.Write | GattProperty.WriteNoResponse,
                GattPermission.Write);

            var tx = new BluetoothGattCharacteristic(
                TxCharUuid,
                GattProperty.Notify,
                GattPermission.Read);
            var cccd = new BluetoothGattDescriptor(CccdUuid, GattDescriptorPermission.Read | GattDescriptorPermission.Write);
            tx.AddDescriptor(cccd);

            service.AddCharacteristic(rx);
            service.AddCharacteristic(tx);

            _txChar = tx;
            _gattServer = server;
            server.AddService(service);
            NotifyStatus("Pigeonnier ouvert : En attente de voisins...");
        }
        catch (Exception ex)
        {
            NotifyStatus($"Pigeonnier BLE erreur : {ex.Message}");
        }
    }

    private void CloseGattServer()
    {
        var server = _gattServer;
        _gattServer = null;
        _txChar = null;
        lock (_serverMtus) _serverMtus.Clear();
        if (server == null) return;
        try { server.ClearServices(); } catch { }
        try { server.Close(); } catch { }
    }

    /// <summary>
    /// Un pair (client GATT) s'abonne à nos notifications : c'est l'équivalent du Accept() RFCOMM.
    /// Renvoie false si on est déjà occupé avec un autre pair (le pair réessaiera plus tard).
    /// </summary>
    internal bool TryAcceptServerSession(BluetoothDevice device)
    {
        string? address = device.Address;
        lock (_sessionLock)
        {
            if (_role != SessionRole.None)
            {
                // Déjà en session (client ou serveur) : on refuse ce doublon
                return false;
            }

            _role = SessionRole.Server;
            _serverPeer = device;
            _peerAddress = address;
            int mtu = DefaultMtu;
            if (address != null)
            {
                lock (_serverMtus) { _serverMtus.TryGetValue(address, out mtu); }
            }
            _sessionMtu = mtu > 0 ? mtu : DefaultMtu;
        }
        return true;
    }

    internal void OnServerSessionAccepted(BluetoothDevice device)
    {
        PeerConnected?.Invoke();
        ResetSessionTimer(6000);
        if (device.Address != null)
        {
            _bleManager.MarkSynced(device.Address);
        }
        NotifyStatus($"Pigeon connecté : {device.Address} !");
    }

    internal void OnServerMtuChanged(BluetoothDevice device, int mtu)
    {
        if (device.Address == null) return;
        lock (_serverMtus) { _serverMtus[device.Address] = mtu; }
        lock (_sessionLock)
        {
            if (_role == SessionRole.Server && string.Equals(_peerAddress, device.Address, StringComparison.OrdinalIgnoreCase))
            {
                _sessionMtu = mtu;
            }
        }
    }

    internal void OnServerDataReceived(BluetoothDevice device, byte[]? value)
    {
        if (value == null || value.Length == 0) return;
        DeliverIncoming(value);
    }

    internal void OnServerPeerDisconnected(BluetoothDevice device)
    {
        if (device.Address != null)
        {
            lock (_serverMtus) { _serverMtus.Remove(device.Address); }
        }

        bool isCurrentPeer;
        lock (_sessionLock)
        {
            isCurrentPeer = _role == SessionRole.Server &&
                            string.Equals(_peerAddress, device.Address, StringComparison.OrdinalIgnoreCase);
        }
        if (isCurrentPeer) EndSession("Connexion fermée avec le pair.");
    }

    internal void OnChunkSent(bool success)
    {
        _pendingChunk?.TrySetResult(success);
    }

    internal BluetoothGattServer? GattServer => _gattServer;

    // ============================================================
    //  PIGEON VOYAGEUR : client GATT (remplace CreateInsecureRfcommSocket + Connect)
    // ============================================================

    /// <summary>
    /// Surcharge Manuelle : pour l'interface de test (bouton de l'écran)
    /// </summary>
    public async Task ConnectToDeviceAsync(string deviceName)
    {
        if (_adapter == null) return;
        var device = _adapter.BondedDevices?.FirstOrDefault(d => d.Name == deviceName);
        if (device != null)
        {
            await ConnectToDeviceAsync(device);
        }
        else
        {
            StatusChanged?.Invoke($"Appareil '{deviceName}' non trouvé dans les appareils appairés.");
        }
    }

    /// <summary>
    /// Mode Client : Se connecte activement à un appareil découvert précis (via son phare BLE).
    /// </summary>
    public async Task ConnectToDeviceAsync(BluetoothDevice device)
    {
        if (IsConnected || _isConnecting) return;
        if (_adapter == null || device == null) return;

        if (!await _connectLock.WaitAsync(0))
        {
            return;
        }

        BluetoothGatt? gatt = null;
        TellMeGattClientCallback? callback = null;
        try
        {
            if (IsConnected) return;
            _isConnecting = true;
            NotifyStatus($"Connexion vers {device.Address} en cours...");

            // Temporisation aléatoire légère (jitter) pour éviter les collisions si les 2 pairs se connectent simultanément
            await Task.Delay(System.Random.Shared.Next(60, 250));
            if (IsConnected) return;

            callback = new TellMeGattClientCallback(this);
            var context = global::Android.App.Application.Context;
            gatt = device.ConnectGatt(context, false, callback, BluetoothTransports.Le);
            if (gatt == null)
            {
                throw new InvalidOperationException("ConnectGatt a renvoyé null");
            }

            var completed = await Task.WhenAny(callback.Ready.Task, Task.Delay(ConnectTimeoutMs));
            bool ready = completed == callback.Ready.Task && callback.Ready.Task.Result;
            if (!ready)
            {
                throw new TimeoutException(callback.FailureReason ?? "délai de connexion GATT dépassé");
            }

            lock (_sessionLock)
            {
                if (_role != SessionRole.None)
                {
                    // Si l'autre côté s'est connecté à notre pigeonnier juste avant nous, on ne garde qu'un seul tuyau
                    callback.Abandon();
                    SafeCloseGatt(gatt);
                    return;
                }

                _role = SessionRole.Client;
                _clientGatt = gatt;
                _clientRxChar = callback.RxChar;
                _peerAddress = device.Address;
                _sessionMtu = callback.Mtu;
            }

            try { gatt.RequestConnectionPriority(GattConnectionPriority.High); } catch { }

            PeerConnected?.Invoke();
            ResetSessionTimer(6000);
            _bleManager.MarkSynced(device.Address ?? "");
            NotifyStatus($"Connecté avec succès à {device.Address} !");

            // Livrer les octets éventuellement reçus pendant la poignée de main (le pair a pu parler en premier)
            callback.Activate();
        }
        catch (Exception ex)
        {
            callback?.Abandon();
            SafeCloseGatt(gatt);
            _bleManager.MarkAttemptFailed(device.Address ?? "");
            NotifyStatus($"Échec de connexion : {ex.Message}");
        }
        finally
        {
            _isConnecting = false;
            _connectLock.Release();
        }
    }

    internal void OnClientDataReceived(byte[] data) => DeliverIncoming(data);

    internal void OnClientDisconnected(BluetoothGatt gatt)
    {
        bool isCurrent;
        lock (_sessionLock)
        {
            isCurrent = _role == SessionRole.Client && ReferenceEquals(_clientGatt, gatt);
        }
        if (isCurrent) EndSession("Connexion fermée avec le pair.");
    }

    // ============================================================
    //  ENVOI (remplace OutputStream.Write)
    // ============================================================

    /// <summary>
    /// Envoie des octets vers l'autre smartphone, découpés en fragments de (MTU - 3) octets.
    /// Un seul fragment est en vol à la fois (contrainte de la pile GATT Android).
    /// </summary>
    public async Task SendAsync(byte[] data)
    {
        if (!IsConnected || data == null || data.Length == 0)
        {
            return;
        }

        await _writeLock.WaitAsync();
        try
        {
            if (!IsConnected) return;

            int chunkSize = Math.Clamp(_sessionMtu - 3, 20, 480);
            for (int offset = 0; offset < data.Length; offset += chunkSize)
            {
                int len = Math.Min(chunkSize, data.Length - offset);
                byte[] chunk = new byte[len];
                Array.Copy(data, offset, chunk, 0, len);

                if (!await SendChunkAsync(chunk))
                {
                    NotifyStatus("Erreur d'envoi : fragment BLE non acquitté.");
                    return;
                }
            }

            ResetSessionTimer(12000);
            NotifyStatus($"Envoyé avec succès ({data.Length} octets) !");
        }
        catch (Exception ex)
        {
            NotifyStatus($"Erreur d'envoi : {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> SendChunkAsync(byte[] chunk)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool started;

            lock (_sessionLock)
            {
                if (_role == SessionRole.None) return false;
                _pendingChunk = tcs;
                started = StartChunkWrite(chunk);
                if (!started) _pendingChunk = null;
            }

            if (!started)
            {
                // Pile GATT occupée : on patiente un court instant puis on réessaie
                await Task.Delay(20);
                continue;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(ChunkTimeoutMs));
            lock (_sessionLock)
            {
                if (ReferenceEquals(_pendingChunk, tcs)) _pendingChunk = null;
            }
            if (completed == tcs.Task && tcs.Task.Result)
            {
                return true;
            }
            await Task.Delay(30);
        }
        return false;
    }

#pragma warning disable CA1422 // API GATT historiques : conservées pour compatibilité Android 5 → 15
    /// <summary>Doit être appelé sous _sessionLock.</summary>
    private bool StartChunkWrite(byte[] chunk)
    {
        try
        {
            if (_role == SessionRole.Client && _clientGatt != null && _clientRxChar != null)
            {
                _clientRxChar.WriteType = GattWriteType.Default;
                _clientRxChar.SetValue(chunk);
                return _clientGatt.WriteCharacteristic(_clientRxChar);
            }

            if (_role == SessionRole.Server && _gattServer != null && _txChar != null && _serverPeer != null)
            {
                _txChar.SetValue(chunk);
                return _gattServer.NotifyCharacteristicChanged(_serverPeer, _txChar, false);
            }
        }
        catch { }
        return false;
    }
#pragma warning restore CA1422

    public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }

        EndSession(null);
        CloseGattServer();

        NotifyStatus("Pigeon déconnecté (prêt pour un nouveau vol).");
        return Task.CompletedTask;
    }


    /// <summary>
    /// Liste les appareils déjà appairés en Bluetooth dans les paramètres Android.
    /// </summary>
    public List<string> GetPairedDevices()
    {
        if (_adapter == null || _adapter.BondedDevices == null) return new();
        return _adapter.BondedDevices.Select(d => d.Name ?? d.Address ?? "Inconnu").ToList();
    }
    private static TaskCompletionSource<bool>? _permissionTcs;

    public static async Task<bool> EnsurePermissionsAsync()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null) return false;

        var permissions = new List<string>();

        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            permissions.Add(global::Android.Manifest.Permission.BluetoothConnect);
            permissions.Add(global::Android.Manifest.Permission.BluetoothScan);
            permissions.Add(global::Android.Manifest.Permission.BluetoothAdvertise);
        }
        else
        {
            permissions.Add(global::Android.Manifest.Permission.AccessFineLocation);
        }

        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            permissions.Add(global::Android.Manifest.Permission.PostNotifications);
        }

        var missing = permissions.Where(p =>
            activity.CheckSelfPermission(p) != global::Android.Content.PM.Permission.Granted
        ).ToArray();

        if (missing.Length == 0) return true;

        _permissionTcs = new TaskCompletionSource<bool>();
        activity.RequestPermissions(missing, 1001);
        return await _permissionTcs.Task;
    }

    public static void OnPermissionsResult(int requestCode, string[] permissions, global::Android.Content.PM.Permission[] grantResults)
    {
        if (requestCode == 1001)
        {
            bool allGranted = grantResults.Length > 0 && grantResults.All(r => r == global::Android.Content.PM.Permission.Granted);
            _permissionTcs?.TrySetResult(allGranted);
        }
    }

    // ============================================================
    //  Callbacks GATT
    // ============================================================

#pragma warning disable CA1422
    /// <summary>
    /// Callback du client GATT (une instance par tentative de connexion).
    /// Séquence : connexion → MTU → découverte du service → abonnement aux notifications TX → prêt.
    /// </summary>
    private class TellMeGattClientCallback : BluetoothGattCallback
    {
        private readonly AndroidBluetoothTransport _transport;
        private readonly object _lock = new();
        private readonly List<byte[]> _earlyData = new();
        private bool _active;
        private bool _abandoned;

        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BluetoothGattCharacteristic? RxChar { get; private set; }
        public int Mtu { get; private set; } = DefaultMtu;
        public string? FailureReason { get; private set; }

        public TellMeGattClientCallback(AndroidBluetoothTransport transport) => _transport = transport;

        private void Fail(string reason)
        {
            FailureReason = reason;
            Ready.TrySetResult(false);
        }

        /// <summary>La session est enregistrée : on livre les octets arrivés pendant la poignée de main.</summary>
        public void Activate()
        {
            List<byte[]> pending;
            lock (_lock)
            {
                _active = true;
                pending = new List<byte[]>(_earlyData);
                _earlyData.Clear();
            }
            foreach (var chunk in pending) _transport.OnClientDataReceived(chunk);
        }

        public void Abandon()
        {
            lock (_lock)
            {
                _abandoned = true;
                _earlyData.Clear();
            }
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (gatt == null) return;

            if (newState == ProfileState.Connected && status == GattStatus.Success)
            {
                // Demander un MTU large (jusqu'à 512 octets utiles par fragment)
                bool requested = false;
                try { requested = gatt.RequestMtu(DesiredMtu); } catch { }
                if (!requested)
                {
                    if (!gatt.DiscoverServices()) Fail("découverte des services impossible");
                }
            }
            else if (newState == ProfileState.Disconnected)
            {
                if (!Ready.Task.IsCompleted)
                {
                    Fail($"déconnecté pendant la poignée de main (statut {(int)status})");
                }
                else
                {
                    _transport.OnClientDisconnected(gatt);
                }
            }
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            if (gatt == null) return;
            Mtu = status == GattStatus.Success && mtu >= DefaultMtu ? mtu : DefaultMtu;
            if (!gatt.DiscoverServices()) Fail("découverte des services impossible");
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (gatt == null) return;
            if (status != GattStatus.Success)
            {
                Fail($"découverte des services échouée (statut {(int)status})");
                return;
            }

            var service = gatt.GetService(TellMeUuid);
            var rx = service?.GetCharacteristic(RxCharUuid);
            var tx = service?.GetCharacteristic(TxCharUuid);
            var cccd = tx?.GetDescriptor(CccdUuid);
            if (service == null || rx == null || tx == null || cccd == null)
            {
                Fail("service TellMe introuvable chez le pair");
                return;
            }

            RxChar = rx;
            gatt.SetCharacteristicNotification(tx, true);
            cccd.SetValue(EnableNotificationValue);
            if (!gatt.WriteDescriptor(cccd))
            {
                Fail("abonnement aux notifications impossible");
            }
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
        {
            if (status == GattStatus.Success)
            {
                Ready.TrySetResult(true);
            }
            else
            {
                // Le pigeonnier distant est occupé avec un autre pair : on réessaiera au prochain tour
                Fail("pigeonnier distant occupé");
            }
        }

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            _transport.OnChunkSent(status == GattStatus.Success);
        }

        // Android < 13
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (characteristic == null || !TxCharUuid.Equals(characteristic.Uuid)) return;
            var value = characteristic.GetValue();
            if (value != null) HandleIncoming((byte[])value.Clone());
        }

        // Android 13+
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
        {
            if (characteristic == null || !TxCharUuid.Equals(characteristic.Uuid)) return;
            if (value != null) HandleIncoming(value);
        }

        private void HandleIncoming(byte[] value)
        {
            lock (_lock)
            {
                if (_abandoned) return;
                if (!_active)
                {
                    _earlyData.Add(value);
                    return;
                }
            }
            _transport.OnClientDataReceived(value);
        }
    }

    /// <summary>
    /// Callback du pigeonnier (serveur GATT).
    /// </summary>
    private class TellMeGattServerCallback : BluetoothGattServerCallback
    {
        private readonly AndroidBluetoothTransport _transport;
        public TellMeGattServerCallback(AndroidBluetoothTransport transport) => _transport = transport;

        public override void OnConnectionStateChange(BluetoothDevice? device, ProfileState status, ProfileState newState)
        {
            if (device == null) return;
            if (newState == ProfileState.Disconnected)
            {
                _transport.OnServerPeerDisconnected(device);
            }
        }

        public override void OnMtuChanged(BluetoothDevice? device, int mtu)
        {
            if (device != null) _transport.OnServerMtuChanged(device, mtu);
        }

        public override void OnDescriptorWriteRequest(BluetoothDevice? device, int requestId, BluetoothGattDescriptor? descriptor,
            bool preparedWrite, bool responseNeeded, int offset, byte[]? value)
        {
            if (device == null) return;
            var server = _transport.GattServer;

            bool isCccd = descriptor != null && CccdUuid.Equals(descriptor.Uuid);
            bool enable = value != null && value.Length >= 1 && (value[0] & 0x01) != 0;
            bool accepted = true;

            if (isCccd)
            {
                try { descriptor?.SetValue(value); } catch { }
                if (enable)
                {
                    // Abonnement = demande d'ouverture de session (équivalent Accept RFCOMM)
                    accepted = _transport.TryAcceptServerSession(device);
                }
            }

            if (responseNeeded)
            {
                try { server?.SendResponse(device, requestId, accepted ? GattStatus.Success : GattStatus.Failure, offset, value); } catch { }
            }

            if (isCccd && enable && accepted)
            {
                // Déclenché après l'acquittement, hors du thread binder
                _ = Task.Run(() => _transport.OnServerSessionAccepted(device));
            }
        }

        public override void OnCharacteristicWriteRequest(BluetoothDevice? device, int requestId, BluetoothGattCharacteristic? characteristic,
            bool preparedWrite, bool responseNeeded, int offset, byte[]? value)
        {
            if (device == null) return;
            if (responseNeeded)
            {
                try { _transport.GattServer?.SendResponse(device, requestId, GattStatus.Success, offset, null); } catch { }
            }

            if (characteristic != null && RxCharUuid.Equals(characteristic.Uuid) && value != null)
            {
                _transport.OnServerDataReceived(device, (byte[])value.Clone());
            }
        }

        public override void OnNotificationSent(BluetoothDevice? device, GattStatus status)
        {
            _transport.OnChunkSent(status == GattStatus.Success);
        }

        public override void OnServiceAdded(GattStatus status, BluetoothGattService? service)
        {
            if (status != GattStatus.Success)
            {
                _transport.NotifyStatus($"Pigeonnier BLE : ajout du service échoué ({(int)status})");
            }
        }
    }
#pragma warning restore CA1422
}

public class BluetoothStateReceiver : global::Android.Content.BroadcastReceiver
{
    private readonly AndroidBluetoothTransport _transport;
    public BluetoothStateReceiver(AndroidBluetoothTransport transport) => _transport = transport;

    public override void OnReceive(global::Android.Content.Context? context, global::Android.Content.Intent? intent)
    {
        if (intent?.Action == BluetoothAdapter.ActionStateChanged)
        {
            var state = (State)intent.GetIntExtra(BluetoothAdapter.ExtraState, BluetoothAdapter.Error);
            if (state == State.On)
            {
                _transport.OnBluetoothStateChanged(true);
            }
            else if (state == State.Off || state == State.TurningOff)
            {
                _transport.OnBluetoothStateChanged(false);
            }
        }
    }
}
