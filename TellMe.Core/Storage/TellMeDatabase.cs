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

    public async Task<Contact?> GetContactAsync(string publicKey){
        return await _database.Table<Contact>().FirstOrDefaultAsync(c => c.PublicKey == publicKey);
    }

    public async Task SaveMessageAsync(
        MeshMessage message, 
        bool isPersonal, 
        bool isOrphan, 
        MessageDeliveryStatus? status = null, 
        bool isAck = false, 
        string ackTargetHash = "")
    {
        StoredMessage storedmsg = StoredMessage.FromMeshMessage(message, isPersonal, isOrphan, status, isAck, ackTargetHash);
        await InsertStoredMessageAsync(storedmsg);
    }

    public async Task InsertStoredMessageAsync(StoredMessage stored) {
        await _database.InsertOrReplaceAsync(stored); 
    }

    public async Task DeleteStoredMessageAsync(string hash)
    {
        await _database.ExecuteAsync("DELETE FROM StoredMessage WHERE Hash = ?", hash);
    }

    public async Task<List<StoredMessage>> GetQueuedMessagesForContactAsync(string to)
    {
        return await _database.Table<StoredMessage>()
            .Where(m => m.To == to && m.DeliveryStatus == MessageDeliveryStatus.Queued)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<StoredMessage>> GetInFlightMessagesForContactAsync(string to)
    {
        return await _database.Table<StoredMessage>()
            .Where(m => m.To == to && m.DeliveryStatus == MessageDeliveryStatus.InFlight)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateMessageDeliveryStatusAsync(string hash, MessageDeliveryStatus status)
    {
        var msg = await _database.Table<StoredMessage>().FirstOrDefaultAsync(m => m.Hash == hash);
        if (msg != null)
        {
            msg.DeliveryStatus = status;
            await _database.UpdateAsync(msg);
        }
    }

    public async Task MarkMessagesAsDeliveredForContactAsync(string contactPublicKey, string? targetHash = null)
    {
        if (!string.IsNullOrEmpty(targetHash))
        {
            var target = await _database.Table<StoredMessage>().FirstOrDefaultAsync(m => m.Hash == targetHash);
            if (target != null)
            {
                target.DeliveryStatus = MessageDeliveryStatus.Delivered;
                await _database.UpdateAsync(target);
            }
        }
        else
        {
            var inFlight = await GetInFlightMessagesForContactAsync(contactPublicKey);
            foreach (var msg in inFlight)
            {
                msg.DeliveryStatus = MessageDeliveryStatus.Delivered;
                await _database.UpdateAsync(msg);
            }
        }
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

    /// <summary>
    /// Récupère les messages d'une conversation spécifique (Board ou Contact direct) de manière paginée.
    /// Les messages sont retournés du plus récent au plus ancien (DESC) jusqu'à 'limit'.
    /// Si 'beforeTimestamp' > 0, ne récupère que les messages strictement antérieurs à cette date.
    /// </summary>
    public async Task<List<StoredMessage>> GetConversationMessagesPagedAsync(
        string conversationKey, 
        bool isBoard, 
        string? myId, 
        int limit = 25, 
        long beforeTimestamp = 0)
    {
        if (isBoard)
        {
            if (beforeTimestamp > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(beforeTimestamp).UtcDateTime;
                string query = @"SELECT * FROM StoredMessage 
                                  WHERE [To] = ? 
                                    AND (IsAck = 0 OR IsAck IS NULL)
                                    AND (CreatedAt < ? OR (CreatedAt = 0 AND ReceivedAt < ?))
                                  ORDER BY CASE WHEN CreatedAt > 0 THEN CreatedAt ELSE 0 END DESC, ReceivedAt DESC 
                                  LIMIT ?";
                return await _database.QueryAsync<StoredMessage>(query, conversationKey, beforeTimestamp, dt, limit);
            }
            else
            {
                string query = @"SELECT * FROM StoredMessage 
                                  WHERE [To] = ? 
                                    AND (IsAck = 0 OR IsAck IS NULL)
                                  ORDER BY CASE WHEN CreatedAt > 0 THEN CreatedAt ELSE 0 END DESC, ReceivedAt DESC 
                                  LIMIT ?";
                return await _database.QueryAsync<StoredMessage>(query, conversationKey, limit);
            }
        }
        else
        {
            string safeMyId = myId ?? string.Empty;
            if (beforeTimestamp > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(beforeTimestamp).UtcDateTime;
                string query = @"SELECT * FROM StoredMessage 
                                  WHERE (([To] = ? AND [From] = ?) OR ([From] = ? AND [To] = ?))
                                    AND (IsAck = 0 OR IsAck IS NULL)
                                    AND (CreatedAt < ? OR (CreatedAt = 0 AND ReceivedAt < ?))
                                  ORDER BY CASE WHEN CreatedAt > 0 THEN CreatedAt ELSE 0 END DESC, ReceivedAt DESC 
                                  LIMIT ?";
                return await _database.QueryAsync<StoredMessage>(query, conversationKey, safeMyId, conversationKey, safeMyId, beforeTimestamp, dt, limit);
            }
            else
            {
                string query = @"SELECT * FROM StoredMessage 
                                  WHERE (([To] = ? AND [From] = ?) OR ([From] = ? AND [To] = ?))
                                    AND (IsAck = 0 OR IsAck IS NULL)
                                  ORDER BY CASE WHEN CreatedAt > 0 THEN CreatedAt ELSE 0 END DESC, ReceivedAt DESC 
                                  LIMIT ?";
                return await _database.QueryAsync<StoredMessage>(query, conversationKey, safeMyId, conversationKey, safeMyId, limit);
            }
        }
    }
}