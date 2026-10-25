using TellMe.Core.Models;
using TellMe.Core.Security;
using TellMe.Core.Network;
using TellMe.Core.Storage;

namespace TellMe.Core.Engine;

public class TellMeEngine{

    public TellMeDatabase _database{get;set;}
    public ITransport _transport {get;set;}
    public SyncEngine _syncEngine{get;set;}
    public Account? _myaccount {get;set;}
    public MessageStore _store{get;set;}
    private ContactQrCard? _pendingCard;
    private ContactRequest? _pendingRequest;
    private ContactResponse? _pendingResponse;

    private readonly HashSet<string> _sentPeersForPendingRequest = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<Contact>? ContactAdded;
    public event Action<Contact, List<StoredMessage>>? PigeonTookOff;
    public event Action<Contact>? PigeonReturned;
    public event Action<string>? MessageDelivered;
    public event Action<StoredMessage>? MessageQueued;

    public TellMeEngine(TellMeDatabase database, ITransport transport){

        _database = database;
        _transport = transport;
        _store = new MessageStore();
        _syncEngine = new SyncEngine(_store,_transport);
        _syncEngine.MyAccountIdProvider = () => _myaccount?.Id;
        _syncEngine.LogAction = (msg) =>
        {
            #if ANDROID
            global::Android.Util.Log.Info("TellMe", $"[Sync] {msg}");
            #else
            System.Diagnostics.Debug.WriteLine($"[Sync] {msg}");
            #endif
        };
        _syncEngine.SyncCompleted += () => _transport.OnSyncCompleted();
        _store.MessageAdded += async (msg) =>
        {
            bool isPersonal = _myaccount != null && (msg.To == _myaccount.Id || msg.From == _myaccount.Id);
            bool isFromMe = _myaccount != null && msg.From == _myaccount.Id;
            bool isToMe = _myaccount != null && msg.To == _myaccount.Id;

            if (isFromMe)
            {
                // Message que nous avons nous-même initié. Déjà géré et persisté par SendMessageAsync.
                return;
            }

            if (!isPersonal)
            {
                // Message de transit (relayé pour le mesh)
                await _database.SaveMessageAsync(msg, isPersonal: false, isOrphan: false);
                return;
            }

            if (isToMe)
            {
                var contact = await _database.GetContactAsync(msg.From);
                if (contact != null && !string.IsNullOrEmpty(contact.SharedAesKey))
                {
                    string decrypted = string.Empty;
                    try
                    {
                        decrypted = CryptoService.Decrypt(msg.Message, contact.SharedAesKey);
                    }
                    catch
                    {
                        decrypted = string.Empty;
                    }

                    if (decrypted.StartsWith("ACK:"))
                    {
                        // ========================================================
                        // C'EST UN ACCUSÉ DE RÉCEPTION REÇU DU DESTINATAIRE !
                        // ========================================================
                        string ackedHash = decrypted.Substring(4);
                        await _database.SaveMessageAsync(
                            msg, 
                            isPersonal: true, 
                            isOrphan: false, 
                            status: MessageDeliveryStatus.Received, 
                            isAck: true, 
                            ackTargetHash: ackedHash);

                        // Marquer le message visé comme Livré
                        await _database.MarkMessagesAsDeliveredForContactAsync(contact.PublicKey, ackedHash);
                        MessageDelivered?.Invoke(ackedHash);

                        // Vérifier la file d'attente pour ce contact
                        await _sendLock.WaitAsync();
                        try
                        {
                            var queuedMessages = await _database.GetQueuedMessagesForContactAsync(contact.PublicKey);
                            if (queuedMessages.Count > 0)
                            {
                                // ================================================
                                // LE PIGEON REPART AVEC TOUTE LA FILE D'ATTENTE !
                                // ================================================
                                var launchedList = new List<StoredMessage>();
                                foreach (var qMsg in queuedMessages)
                                {
                                    string oldTempHash = qMsg.Hash;

                                    var meshMsg = MeshMessage.CreateSigned(
                                        qMsg.Message, 
                                        qMsg.To, 
                                        _store.Heads.ToArray(), 
                                        _myaccount!.Id, 
                                        _myaccount.PrivateKey);

                                    _store.AddMessage(meshMsg);

                                    await _database.DeleteStoredMessageAsync(oldTempHash);

                                    qMsg.Hash = meshMsg.Hash;
                                    qMsg.ParentsJoined = string.Join(",", meshMsg.Parents);
                                    qMsg.Signature = meshMsg.Signature;
                                    qMsg.DeliveryStatus = MessageDeliveryStatus.InFlight;
                                    await _database.InsertStoredMessageAsync(qMsg);
                                    launchedList.Add(qMsg);
                                }

                                contact.PigeonState = "InFlight";
                                contact.FlightStartedAt = DateTime.UtcNow;
                                contact.MessagesInFlightCount = queuedMessages.Count;
                                await _database.SaveContactAsync(contact);

                                _transport.ResetCooldown();
                                await _syncEngine.StartSyncAsync();

                                PigeonTookOff?.Invoke(contact, launchedList);
                            }
                            else
                            {
                                // Aucun message en attente : le pigeon se pose au perchoir
                                contact.PigeonState = "AtPerch";
                                contact.FlightStartedAt = null;
                                contact.MessagesInFlightCount = 0;
                                await _database.SaveContactAsync(contact);

                                PigeonReturned?.Invoke(contact);
                            }
                        }
                        finally
                        {
                            _sendLock.Release();
                        }

                        return;
                    }
                    else
                    {
                        // ========================================================
                        // MESSAGE TEXTE REÇU D'UN CONTACT -> ÉMETTRE L'ACK !
                        // ========================================================
                        await _database.SaveMessageAsync(
                            msg, 
                            isPersonal: true, 
                            isOrphan: false, 
                            status: MessageDeliveryStatus.Received);

                        // Émettre automatiquement l'accusé de réception utilitaire
                        string ackPayload = $"ACK:{msg.Hash}";
                        string ackEncrypted = CryptoService.Encrypt(ackPayload, contact.SharedAesKey);
                        var ackMsg = MeshMessage.CreateSigned(
                            ackEncrypted, 
                            msg.From, 
                            new [] { msg.Hash }, 
                            _myaccount!.Id, 
                            _myaccount.PrivateKey);

                        _store.AddMessage(ackMsg);
                        await _database.SaveMessageAsync(
                            ackMsg, 
                            isPersonal: true, 
                            isOrphan: false, 
                            status: MessageDeliveryStatus.InFlight, 
                            isAck: true, 
                            ackTargetHash: msg.Hash);

                        _transport.ResetCooldown();
                        await _syncEngine.StartSyncAsync();
                        return;
                    }
                }

                // Message direct sans clé ou board public
                await _database.SaveMessageAsync(msg, isPersonal: true, isOrphan: false, status: MessageDeliveryStatus.Received);
            }
        };
        _syncEngine.ContactResponseReceived += async (response) => //on s'abonne aux reponses
        {
            if (response.ContactAccepted && _pendingCard != null && _pendingRequest != null)
            {   //on verifie que la reponse est authentique
                bool isValid = CryptoService.Verify($"{_pendingRequest.RequestHash}|accepted",response.ResponseSignature,_pendingCard.id);
                if (isValid){
                    var newContact = new Contact{
                        PublicKey = _pendingCard.id,
                        DisplayName = _pendingCard.DisplayName,
                        PigeonName = _pendingCard.PigeonName,
                        SharedAesKey = _pendingRequest.SharedAesKey
                    };
                    await _database.SaveContactAsync(newContact);
                    ContactAdded?.Invoke(newContact);
                    _pendingCard = null;
                    _pendingRequest = null;
                    lock (_sentPeersForPendingRequest)
                    {
                        _sentPeersForPendingRequest.Clear();
                    }
                }
            }
        };

        _transport.PeerConnected += async () =>
        {
            if (_pendingRequest != null)
            {
                await _syncEngine.SendContactRequestAsync(_pendingRequest);
            }
            if (_pendingResponse != null)
            {
                await _syncEngine.SendContactResponseAsync(_pendingResponse);
                _pendingResponse = null;
            }
        };

    }

    public async Task InitializeAsync(string defaultDisplayName = "Moi"){
        await _database.InitializeAsync();
        _myaccount = await _database.GetAccountAsync();//on recupere le compte utilisateur
        if (_myaccount == null){ 
            _myaccount = new Account(defaultDisplayName);
            await _database.SaveAccountAsync(_myaccount); //si le user n'a pas de compte, on lui en cree un et on sauvegarde;
        }

        var messages = await _database.GetAllStoredMessagesAsync(); //on recupere tous les messages de la base
        foreach (var stored in messages){
            _store.AddMessage(stored.ToMeshMessage(), verifySignature: false); // Déjà validés en base, chargement ultra-rapide
        }

        if (_myaccount != null && !string.IsNullOrEmpty(_myaccount.Id))
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_myaccount.Id)).Take(4).ToArray();
            _transport.SetMyAccountHash(hash);
        }

        await _transport.StartAsync();

    }

    public async Task<MeshMessage> SendMessageAsync(string content, string to){
        await _sendLock.WaitAsync();
        try
        {
            var contact = await _database.GetContactAsync(to);
            string payload = content;
            if (to.StartsWith("PUBLICBOARD:")){
                payload = content;
                if(contact == null) {
                    string[] parts = to.Split(':');
                    var newBoard = new Contact(to,parts[1]);
                    await _database.SaveContactAsync(newBoard);
                }

                var msg = MeshMessage.CreateSigned(payload, to, _store.Heads.ToArray(), _myaccount!.Id, _myaccount.PrivateKey);
                _store.AddMessage(msg);

                bool isPersonal = _myaccount != null && (msg.To == _myaccount.Id || msg.From == _myaccount.Id);
                await _database.SaveMessageAsync(msg, isPersonal, isOrphan: false, status: MessageDeliveryStatus.Delivered);
                _transport.ResetCooldown();
                await _syncEngine.StartSyncAsync();
                return msg;
            }

            if (contact != null && !string.IsNullOrEmpty(contact.SharedAesKey)){
                payload = CryptoService.Encrypt(content, contact.SharedAesKey);
            }

            // Vérifier l'état du pigeon pour ce contact
            if (contact != null && contact.IsDirectContact && contact.PigeonState == "InFlight")
            {
                // Le pigeon est déjà en vol ! Le message attend au pigeonnier
                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string tempHash = Guid.NewGuid().ToString("N");
                var queued = new StoredMessage
                {
                    Hash = tempHash,
                    Message = payload,
                    From = _myaccount!.Id,
                    To = to,
                    ParentsJoined = "",
                    IsPersonal = true,
                    IsOrphan = false,
                    ReceivedAt = DateTime.UtcNow,
                    CreatedAt = ts,
                    DeliveryStatus = MessageDeliveryStatus.Queued,
                    Signature = ""
                };
                await _database.InsertStoredMessageAsync(queued);
                MessageQueued?.Invoke(queued);
                return queued.ToMeshMessage();
            }

            // Le pigeon est au perchoir : il prend son envol !
            var inFlightMsg = MeshMessage.CreateSigned(payload, to, _store.Heads.ToArray(), _myaccount!.Id, _myaccount.PrivateKey);
            _store.AddMessage(inFlightMsg);

            bool isPers = _myaccount != null && (inFlightMsg.To == _myaccount.Id || inFlightMsg.From == _myaccount.Id);
            await _database.SaveMessageAsync(inFlightMsg, isPers, isOrphan: false, status: MessageDeliveryStatus.InFlight);

            if (contact != null && contact.IsDirectContact)
            {
                contact.PigeonState = "InFlight";
                contact.FlightStartedAt = DateTime.UtcNow;
                contact.MessagesInFlightCount = 1;
                await _database.SaveContactAsync(contact);
                PigeonTookOff?.Invoke(contact, new List<StoredMessage> { StoredMessage.FromMeshMessage(inFlightMsg, true, false, MessageDeliveryStatus.InFlight) });
            }

            _transport.ResetCooldown();
            await _syncEngine.StartSyncAsync();

            return inFlightMsg;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string> GetDecryptedContentAsync(MeshMessage msg) {
        
        var contact = new Contact();
        string content = string.Empty;
        try{

        if (msg.To.StartsWith("PUBLICBOARD:")){
            content = msg.Message;
        }

        if (msg.To.StartsWith("PRIVATEBOARD:")){
            contact = await _database.GetContactAsync(msg.To);
            content = CryptoService.Decrypt(msg.Message,contact.SharedAesKey);
        }

        if (!msg.To.Contains("BOARD") && !string.IsNullOrEmpty(msg.To)){
            if(msg.From == _myaccount.Id){
                contact = await _database.GetContactAsync(msg.To);
                content = CryptoService.Decrypt(msg.Message,contact.SharedAesKey);
            }
            else{
                contact = await _database.GetContactAsync(msg.From);
                content = CryptoService.Decrypt(msg.Message, contact.SharedAesKey);
            }
        }

        if (content.StartsWith("ACK:"))
        {
            return "Accusé de réception";
        }

        return content;
        }
        catch{return "erreur lors du dechiffrage du message";}
    }

    public async Task<ContactRequest> AddContactFromScannedCardAsync(ContactQrCard card)
    {
        if (_myaccount == null) throw new InvalidOperationException("TellMeEngine non initialisé");

        _pendingCard = card;
        _pendingRequest = new ContactRequest(_myaccount, card.id);
        lock (_sentPeersForPendingRequest)
        {
            _sentPeersForPendingRequest.Clear();
        }
        _transport.ResetCooldown();

        // Enclencher la priorité directe vers l'ami scanné (recherche par empreinte BLE / RSSI et retry rapide)
        await _transport.PrioritizeTargetAsync(card.BluetoothAddress, card.id);

        if (_transport.IsConnected)
        {
            string? peerAddr = _transport.ConnectedPeerAddress;
            lock (_sentPeersForPendingRequest)
            {
                if (!string.IsNullOrEmpty(peerAddr))
                {
                    _sentPeersForPendingRequest.Add(peerAddr);
                }
            }
            await _syncEngine.SendContactRequestAsync(_pendingRequest);
        }
        return _pendingRequest;
    }

    public async Task AcceptContactRequestAsync(ContactRequest request){
        if (_myaccount == null)
            throw new InvalidOperationException("TellMeEngine non initialisé.");
        // 1. Sauvegarder le demandeur dans nos contacts avec la clé AES commune
        var newContact = new Contact
        {
            PublicKey = request.RequesterId,
            DisplayName = request.Name,
            PigeonName = request.Pigeon,
            SharedAesKey = request.SharedAesKey
        };
        await _database.SaveContactAsync(newContact);
        ContactAdded?.Invoke(newContact);

        // 2. Mémoriser la réponse signée pour renvoi garanti si déconnexion
        var response = new ContactResponse(request.RequestHash, true, _myaccount);
        _pendingResponse = response;
        _transport.ResetCooldown();

        // 3. Envoyer la réponse
        await _syncEngine.SendContactResponseAsync(response);
    }

}