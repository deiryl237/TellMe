using SQLite;
using TellMe.Core.Models;

namespace TellMe.Core.Storage;

public class TellMeDatabase{

    private readonly SQLiteAsyncConnection _database;

    public TellMeDatabase(string dbPath){
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync(){

        await _database.CreateTableAsync<Account>();
        await _database.CreateTableAsync<StoredMessage>();
        await _database.CreateTableAsync<Contact>();

    }

    public async Task SaveAccountAsync(Account account){
        await _database.InsertOrReplaceAsync(account); //créer un compte 
    }

    public async Task SaveContactAsync(Contact contact){
        await _database.InsertOrReplaceAsync(contact);
    }

    public async Task<Account?> GetAccountAsync(){
        Account account = await _database.Table<Account>().FirstOrDefaultAsync();//Récupérer le compte de l'utilisateur et le renvoyer, renvoit null si pas de compte
        return account;
    }
    public async Task<Account?> GetAccountSpecAsync(string publicKey){
        Account account = await _database.Table<Account>().FirstOrDefaultAsync(c => c.Id == publicKey);//Récupérer le compte de l'utilisateur et le renvoyer, renvoit null si pas de compte
        return account;
    }

    public async Task<Contact?> GetContactAsync(string publicKey){
        return await _database.Table<Contact>().FirstOrDefaultAsync(c => c.PublicKey == publicKey);
    }

    public async Task SaveMessageAsync(MeshMessage message, bool isPersonal, bool isOrphan){//Sauvegarder un MeshMessage dans la table StoredMessage
        StoredMessage storedmsg = StoredMessage.FromMeshMessage(message,isPersonal,isOrphan);
        await InsertStoredMessageAsync(storedmsg);
    }

    public async Task InsertStoredMessageAsync(StoredMessage stored) {//méthode utilitaire pour insérér un message dans la table StoredMessage 
        await _database.InsertOrReplaceAsync(stored); 
    }

    public async Task<List<StoredMessage>> GetAllStoredMessagesAsync(){
        List <StoredMessage> stored = await _database.Table<StoredMessage>().ToListAsync();
        return stored;

    }

    public async Task<List<MeshMessage>> GetAllMessagesAsync(){
        var stored = await GetAllStoredMessagesAsync();
        List <MeshMessage> meshmsg = stored.Select(s => s.ToMeshMessage()).ToList();
        return meshmsg;
    }

    public async Task<List<Contact>> GetAllContactsAsync(){
        return await _database.Table<Contact>().ToListAsync();
    }

    public async Task EvictTransitMessagesAsync (DateTime CutOff){

        await _database.ExecuteAsync(
        "DELETE FROM StoredMessage WHERE IsPersonal = 0 AND ReceivedAt < ?", CutOff);


    }
}