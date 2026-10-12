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

    public StoredMessage(){

    }

    public MeshMessage ToMeshMessage(){

       string[] parents = string.IsNullOrEmpty(ParentsJoined)?Array.Empty<string>() : ParentsJoined.Split(',');
       MeshMessage meshmessage = new MeshMessage(Message,From,To,parents);
       return meshmessage;

    }
    public static StoredMessage FromMeshMessage(MeshMessage msg,bool isPersonal,bool isOrphan){
        return new StoredMessage{
            Message = msg.Message,
            Hash = msg.Hash,
            ParentsJoined = string.Join(",",msg.Parents),
            From = msg.From,
            To = msg.To,
            IsPersonal = isPersonal,
            IsOrphan = isOrphan
        };
    }


}

