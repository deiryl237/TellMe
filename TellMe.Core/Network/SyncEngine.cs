using TellMe.Core.Engine;
using TellMe.Core.Models;
using System.Text.Json;
using TellMe.Core.Security;

namespace TellMe.Core.Network;
public class SyncEngine{

    private readonly MessageStore _store;
    private readonly ITransport _transport;
    public event Action<ContactRequest>? ContactRequestReceived;
    public event Action<ContactResponse>? ContactResponseReceived;
    public event Action? SyncCompleted;
    public Func<string?>? MyAccountIdProvider { get; set; }
    public Action<string>? LogAction { get; set; }

    private readonly List<byte> _receiveBuffer = new();
    private readonly object _bufferLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly HashSet<string> _handledContactRequests = new(StringComparer.OrdinalIgnoreCase);

    private System.Threading.Timer? _inactivityTimer;
    private readonly object _timerLock = new();
    private volatile bool _isContactExchangePending = false;

    private void RestartInactivityTimer(int delayMs = 650)
    {
        lock (_timerLock)
        {
            _inactivityTimer?.Dispose();
            // Pendant un échange de contact (l'humain doit voir la popup et cliquer "Accepter"),
            // on accorde jusqu'à 6 secondes pour ne pas figer le réseau indéfiniment.
            int effectiveDelay = _isContactExchangePending ? Math.Max(delayMs, 6000) : delayMs;
            _inactivityTimer = new System.Threading.Timer(_ =>
            {
                SyncCompleted?.Invoke();
            }, null, effectiveDelay, Timeout.Infinite);
        }
    }

    public SyncEngine(MessageStore store, ITransport transport){

        _transport = transport;
        _store = store;
        _transport.DataReceived += OnDataReceived;
        _transport.PeerConnected += async () =>
        {
            _isContactExchangePending = false;
            lock (_bufferLock)
            {
                _receiveBuffer.Clear();
            }
            RestartInactivityTimer(500);
            await StartSyncAsync();
        };

    }

    private async Task SendPacketAsync(SyncPacket packet){
        if (!_transport.IsConnected) return;

        RestartInactivityTimer(350);

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(packet);
        byte[] framed = new byte[4 + payload.Length];
        framed[0] = (byte)(payload.Length >> 24);
        framed[1] = (byte)(payload.Length >> 16);
        framed[2] = (byte)(payload.Length >> 8);
        framed[3] = (byte)payload.Length;
        payload.CopyTo(framed, 4);

        await _sendLock.WaitAsync();
        try
        {
            await _transport.SendAsync(framed);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task StartSyncAsync(){

        RestartInactivityTimer(500);
        var data = new SyncPacket();
        data.Type = "Heads";
        data.Heads = _store.Heads.ToArray();
        await SendPacketAsync(data);

    }

    private void OnDataReceived(byte[] data)
    {
        RestartInactivityTimer(350);
        List<byte[]> completePackets = new();
        lock (_bufferLock)
        {
            _receiveBuffer.AddRange(data);

            while (_receiveBuffer.Count > 0)
            {
                // Ignorer les espaces blancs initiaux
                while (_receiveBuffer.Count > 0 && (_receiveBuffer[0] == (byte)' ' || _receiveBuffer[0] == (byte)'\n' || _receiveBuffer[0] == (byte)'\r' || _receiveBuffer[0] == (byte)'\t'))
                {
                    _receiveBuffer.RemoveAt(0);
                }

                if (_receiveBuffer.Count == 0) break;

                // Cas 1 : Flux JSON brut (commence par '{')
                if (_receiveBuffer[0] == (byte)'{')
                {
                    byte[] currentBuffer = _receiveBuffer.ToArray();
                    ReadOnlySpan<byte> span = currentBuffer;
                    try
                    {
                        var reader = new Utf8JsonReader(span);
                        if (!reader.Read()) break;

                        if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            int depth = 0;
                            bool completed = false;
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.StartObject) depth++;
                                else if (reader.TokenType == JsonTokenType.EndObject)
                                {
                                    if (depth == 0)
                                    {
                                        completed = true;
                                        break;
                                    }
                                    depth--;
                                }
                            }

                            if (!completed) break; // Incomplet, attendre le prochain morceau

                            int bytesConsumed = (int)reader.BytesConsumed;
                            if (bytesConsumed > 0 && bytesConsumed <= _receiveBuffer.Count)
                            {
                                byte[] packetBytes = _receiveBuffer.GetRange(0, bytesConsumed).ToArray();
                                _receiveBuffer.RemoveRange(0, bytesConsumed);
                                completePackets.Add(packetBytes);
                                continue;
                            }
                        }
                        break;
                    }
                    catch
                    {
                        // JSON incomplet, on attend la suite du flux
                        break;
                    }
                }

                // Cas 2 : Trame préfixée par sa longueur [4 octets longueur][payload]
                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                int payloadLength = (_receiveBuffer[0] << 24) |
                                    (_receiveBuffer[1] << 16) |
                                    (_receiveBuffer[2] << 8)  |
                                     _receiveBuffer[3];

                if (payloadLength <= 0 || payloadLength > 5 * 1024 * 1024)
                {
                    // Trame invalide ou corruption de flux, réinitialisation de sécurité
                    _receiveBuffer.Clear();
                    break;
                }

                if (_receiveBuffer.Count < 4 + payloadLength)
                {
                    // Le paquet complet n'est pas encore arrivé, on attend le prochain chunk
                    break;
                }

                byte[] packetBytesFramed = _receiveBuffer.GetRange(4, payloadLength).ToArray();
                _receiveBuffer.RemoveRange(0, 4 + payloadLength);
                completePackets.Add(packetBytesFramed);
            }
        }

        if (completePackets.Count > 0)
        {
            RestartInactivityTimer(350);
            _ = Task.Run(async () =>
            {
                await _processLock.WaitAsync();
                try
                {
                    foreach (var packetBytes in completePackets)
                    {
                        try
                        {
                            var packet = JsonSerializer.Deserialize<SyncPacket>(packetBytes);
                            if (packet != null)
                            {
                                await ProcessPacketAsync(packet);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogAction?.Invoke($"Erreur désérialisation paquet: {ex.Message}");
                        }
                    }
                }
                finally
                {
                    _processLock.Release();
                }
            });
        }
    }
    
     private async Task ProcessPacketAsync(SyncPacket packet)
    {
        LogAction?.Invoke($"Paquet reçu: Type={packet.Type}");
        switch (packet.Type)
        {
            case "Heads":
                if (packet.Heads != null)
                {
                    LogAction?.Invoke($"Têtes reçues: {packet.Heads.Length} têtes");
                    bool hasMissing = false;
                    foreach (string head in packet.Heads)
                    {
                        if (!_store.Contains(head))
                        {
                            hasMissing = true;
                            LogAction?.Invoke($"Demande hash manquant: {head}");
                            var datarequest = new SyncPacket();
                            datarequest.Type = "Request";
                            datarequest.RequestedHash = head;
                            await SendPacketAsync(datarequest);
                        }
                    }

                    if (!hasMissing && !_isContactExchangePending)
                    {
                        // Aucun nouveau message nécessaire : switch ultra-rapide (150ms) !
                        RestartInactivityTimer(150);
                    }
                }
                break;
            case "Request":
                if (packet.RequestedHash != null && _store.Contains(packet.RequestedHash))
                {
                    MeshMessage? message = _store.GetMessage(packet.RequestedHash);
                    if (message != null)
                    {
                        LogAction?.Invoke($"Envoi message demandé: {packet.RequestedHash}");
                        var messagepacket = new SyncPacket();
                        messagepacket.Type = "Message";
                        messagepacket.Message = message;
                        await SendPacketAsync(messagepacket);
                    }
                }
                break;
            case "Message":
                if (packet.Message != null)
                {
                    LogAction?.Invoke($"Message synchronisé: Hash={packet.Message.Hash}, To={packet.Message.To}");
                    _store.AddMessage(packet.Message);
                    bool hasMissingParent = false;
                    if (packet.Message.Parents != null)
                    {
                        foreach (string parent in packet.Message.Parents)
                        {
                            if (!_store.Contains(parent))
                            {
                                hasMissingParent = true;
                                LogAction?.Invoke($"Demande parent manquant: {parent}");
                                var request = new SyncPacket();
                                request.Type = "Request";
                                request.RequestedHash = parent;
                                await SendPacketAsync(request);
                            }
                        }
                    }

                    if (!hasMissingParent && !_isContactExchangePending)
                    {
                        // Message reçu et DAG complet : rotation quasi-immédiate (150ms) !
                        RestartInactivityTimer(150);
                    }
                }
                break;

            case "ContactRequest":
            if (packet.request != null){
                var request = packet.request;
                var myId = MyAccountIdProvider?.Invoke();
                if (!string.IsNullOrEmpty(request.TargetId) && !string.IsNullOrEmpty(myId) && request.TargetId != myId)
                {
                    // Ce n'est pas nous qui avons été scannés, cette demande s'adresse à quelqu'un d'autre !
                    break;
                }
                lock (_handledContactRequests)
                {
                    if (_handledContactRequests.Contains(request.RequestHash))
                    {
                        // Déjà reçu et traité (ou en cours de traitement) : ignorer le doublon !
                        break;
                    }
                    _handledContactRequests.Add(request.RequestHash);
                }
                if(CryptoService.Verify(request.RequestHash,request.RequestSignature,request.RequesterId))
                {
                    _isContactExchangePending = true;
                    RestartInactivityTimer(6000);
                    ContactRequestReceived?.Invoke(packet.request);
                }
            }
            break;
            case "ContactResponse":
            if (packet.response != null)
            {
                _isContactExchangePending = false;
                RestartInactivityTimer(650);
                ContactResponseReceived?.Invoke(packet.response);
            }
            break;
        }
    }

    public async Task<ContactRequest> SendContactRequestAsync(Account myAccount){
        var request = new ContactRequest(myAccount);
        await SendContactRequestAsync(request);
        return request;
    }

    public async Task SendContactRequestAsync(ContactRequest request){
        _isContactExchangePending = true;
        RestartInactivityTimer(6000);
        var packet = new SyncPacket();
        packet.Type = "ContactRequest";
        packet.request = request;
        await SendPacketAsync(packet);
    }
    
    public async Task SendContactResponseAsync(ContactResponse response){
        _isContactExchangePending = false;
        var packet = new SyncPacket();
        packet.Type = "ContactResponse";
        packet.response = response;
        await SendPacketAsync(packet);
        RestartInactivityTimer(650);
    }

    public async Task SendContactResponseAsync(string requestHash, bool accepted, Account myAccount){
        var response = new ContactResponse(requestHash,accepted,myAccount);
        await SendContactResponseAsync(response);
    }
}


