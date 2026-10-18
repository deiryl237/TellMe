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

    public TellMeEngine(TellMeDatabase database, ITransport transport){

        _database = database;
        _transport = transport;
        _store = new MessageStore();
        _syncEngine = new SyncEngine(_store,_transport);
        _store.MessageAdded += async (msg) =>{
            bool isPersonal = _myaccount != null && (msg.To == _myaccount.Id || msg.From == _myaccount.Id);
            await _database.SaveMessageAsync(msg,isPersonal,isOrphan: false); //on ecoute l'event messageadded du store, à chaque fois 
            // qu'un nouveau message entre dans le store on l'enregistre en base.
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
                }
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
            _store.AddMessage(stored.ToMeshMessage());//on les charge le store
        }
        await _transport.StartAsync();

    }

    public async Task<MeshMessage> SendMessageAsync(string content, string to){

        var contact = await  _database.GetContactAsync(to) ;
        string payload = content;
        if (to.StartsWith("PUBLICBOARD:")){
            payload = content;//si on envoi un message sur un board public on ne le chiffre pas
            if(contact == null) {
                string[] parts = to.Split(':');
                var newBoard = new Contact(to,parts[1]);
                await _database.SaveContactAsync(newBoard);//et si le board n'est pas deja dans les contacts on l'y ajoute
            }
        }

        else if (contact != null && !string.IsNullOrEmpty(contact.SharedAesKey)){
            payload = CryptoService.Encrypt(content, contact.SharedAesKey);
        }

        var msg = MeshMessage.CreateSigned(payload,to,_store.Heads.ToArray(),_myaccount.Id, _myaccount.PrivateKey);
        _store.AddMessage(msg);

        bool isPersonal = _myaccount != null && (msg.To == _myaccount.Id || msg.From == _myaccount.Id);
        await _database.SaveMessageAsync(msg, isPersonal, isOrphan: false);
        _transport.ResetCooldown();

        await _syncEngine.StartSyncAsync();

        return msg;
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

        return content;
        }
        catch{return "erreur lors du dechiffrage du message";}
    }

    public async Task<ContactRequest> AddContactFromScannedCardAsync(ContactQrCard card)
    {
        if (_myaccount == null) throw new InvalidOperationException("TellMeEngine non initialisé");

        _pendingCard = card;
        _pendingRequest = await _syncEngine.SendContactRequestAsync(_myaccount);
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
        // 2. Envoyer la réponse signée par notre compte
        await _syncEngine.SendContactResponseAsync(request.RequestHash, true, _myaccount);
    }

}