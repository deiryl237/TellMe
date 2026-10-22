using SQLite;

namespace TellMe.Core.Models;

public class StoredMessage{

    [PrimaryKey] public string Hash{get;set;} = string.Empty;
    public string Message {get;set;} = string.Empty;
    public string From {get;set;} = string.Empty;
    public string To {get;set;} = string.Empty;
    public string ParentsJoined {get;set;} = string.Empty;
    [Indexed]public bool IsPersonal {get;set;}
    public bool IsOrphan {get;set;}
    [Indexed]public DateTime ReceivedAt {get;set;} = DateTime.UtcNow;
    [Indexed]public long CreatedAt {get;set;}
    public string Signature { get;set; } = string.Empty;


    public StoredMessage(){

    }

    public MeshMessage ToMeshMessage(){

       string[] parents = string.IsNullOrEmpty(ParentsJoined)?Array.Empty<string>() : ParentsJoined.Split(',');
       long ts = CreatedAt > 0 ? CreatedAt : ((DateTimeOffset)ReceivedAt).ToUnixTimeMilliseconds();
       MeshMessage meshmessage = new MeshMessage(Message,From,To,parents,Signature, ts);
       return meshmessage;

    }
    public static StoredMessage FromMeshMessage(MeshMessage msg,bool isPersonal,bool isOrphan){
        long ts = msg.Timestamp > 0 ? msg.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new StoredMessage{
            Message = msg.Message,
            Hash = msg.Hash,
            ParentsJoined = string.Join(",",msg.Parents),
            From = msg.From,
            To = msg.To,
            IsPersonal = isPersonal,
            IsOrphan = isOrphan,
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = ts,
            Signature = msg.Signature
        };
    }


}

