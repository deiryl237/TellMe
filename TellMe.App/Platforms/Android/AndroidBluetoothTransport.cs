using Android.Bluetooth;
using Java.Util;
using TellMe.Core.Network;

namespace TellMe.App.Platforms.Android;

public class AndroidBluetoothTransport : ITransport
{
    // L'UUID unique qui identifie le service TellMe dans l'air Bluetooth
    private static readonly UUID TellMeUuid = UUID.FromString("a6c8e3b0-28b9-4a4b-bb29-9e7b99c01123")!;
    private const string ServiceName = "TellMePigeonService";
    private readonly BleDiscoveryManager _bleManager;
    private readonly BluetoothAdapter? _adapter;
    private BluetoothServerSocket? _serverSocket;
    private BluetoothSocket? _connectedSocket;
    private CancellationTokenSource? _cts;

    public event Action<byte[]>? DataReceived;
    public event Action? PeerConnected;

    public event Action<string>? StatusChanged; // Pour afficher ce qui se passe sur l'écran

    public AndroidBluetoothTransport()
    {
        _adapter = BluetoothAdapter.DefaultAdapter;
        _bleManager = new BleDiscoveryManager();
        _bleManager.StatusLog += msg => StatusChanged?.Invoke(msg);
        _bleManager.PeerDiscovered += async device =>{
            await ConnectToDeviceAsync(device);
        };
    }

    /// <summary>
    /// Mode Serveur : Se met en écoute et attend qu'un pigeon distant se connecte.
    /// </summary>
    public async Task StartAsync()
    {   
        _bleManager.StartAdvertising(); // Allume le Phare
        _bleManager.StartScanning();    // Allume le Radar

        await StopAsync();
        if (_adapter == null || !_adapter.IsEnabled)
        {
            StatusChanged?.Invoke("Erreur : Bluetooth désactivé ou indisponible.");
            return ;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenForIncomingConnection(_cts.Token));
    }

    private async Task ListenForIncomingConnection(CancellationToken ct)
    {
    while (!ct.IsCancellationRequested && _adapter != null && _adapter.IsEnabled)
    {
        try
        {
            StatusChanged?.Invoke("Pigeonnier ouvert : En attente de voisins...");
            _serverSocket = _adapter.ListenUsingInsecureRfcommWithServiceRecord(ServiceName, TellMeUuid);
            
            var socket = _serverSocket.Accept();
            if (socket != null)
            {
                _connectedSocket = socket;
                PeerConnected?.Invoke();
                try { _serverSocket.Close(); _serverSocket = null; } catch { }
                StatusChanged?.Invoke($"Pigeon connecté : {socket.RemoteDevice?.Address} !");
                await StartReadingLoop(socket, ct);
            }
        }
        catch (Exception)
        {
            // Dès qu'une connexion se termine ou échoue, on attend 1 seconde et la boucle while rouvre le serveur !
            await Task.Delay(1000, ct);
        }
    }
    }

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
    /// Mode Client : Se connecte activement à un appareil appairé précis.
    /// </summary>
    public async Task ConnectToDeviceAsync(BluetoothDevice device)
    {
        await StopAsync();
        if (_adapter == null) return;
        if (device == null)
        {
            return;
        }

        try
        {
            StatusChanged?.Invoke($"Connexion vers {device.Address} en cours...");
            _cts = new CancellationTokenSource();
            var socket = device.CreateInsecureRfcommSocketToServiceRecord(TellMeUuid);
            if (socket == null) return;

            await socket.ConnectAsync();
            _connectedSocket = socket;
            PeerConnected?.Invoke();

            StatusChanged?.Invoke($"Connecté avec succès à {device.Address} !");

            _ = Task.Run(() => StartReadingLoop(socket, _cts.Token));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Échec de connexion : {ex.Message}");
        }
    }

    /// <summary>
    /// Boucle de lecture en tâche de fond : dès que des octets arrivent, déclenche DataReceived.
    /// </summary>
    private async Task StartReadingLoop(BluetoothSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        var stream = socket.InputStream;

        try
        {
            while (!ct.IsCancellationRequested && socket.IsConnected && stream != null)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead > 0)
                {
                    byte[] packet = buffer.Take(bytesRead).ToArray();
                    DataReceived?.Invoke(packet);
                }
            }
        }
        catch (Exception)
        {
            StatusChanged?.Invoke("Connexion perdue avec le pair.");
        }
                finally
        {
            try { socket.Close(); } catch { }
            if (_connectedSocket == socket) _connectedSocket = null;
        }

    }

    /// <summary>
    /// Envoie des octets dans le tuyau vers l'autre smartphone.
    /// </summary>
    public async Task SendAsync(byte[] data)
    {
        if (_connectedSocket == null || !_connectedSocket.IsConnected)
        {
            StatusChanged?.Invoke("Impossible d'envoyer : aucun pigeon connecté.");
            return;
        }

        try
        {
            var stream = _connectedSocket.OutputStream;
            if (stream != null)
            {
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
                StatusChanged?.Invoke($"Envoyé avec succès ({data.Length} octets) !");
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Erreur d'envoi : {ex.Message}");
        }
    }

        public Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
            _connectedSocket?.Close();
            _connectedSocket = null;
            _serverSocket?.Close();
            _serverSocket = null;
        }
        catch { }

        StatusChanged?.Invoke("Pigeon déconnecté (prêt pour un nouveau vol).");
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
    public static void RequestPermissionsIfNeeded()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
            {
                var permissions = new[]
                {
                    global::Android.Manifest.Permission.BluetoothConnect,
                    global::Android.Manifest.Permission.BluetoothScan
                };

                var missing = permissions.Where(p => 
                    activity.CheckSelfPermission(p) != global::Android.Content.PM.Permission.Granted
                ).ToArray();

                if (missing.Length > 0)
                {
                    activity.RequestPermissions(missing, 1001);
                }
            }
        }
    }

}
