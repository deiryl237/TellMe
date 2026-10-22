using SQLite;

namespace TellMe.Core.Models;

public class Contact{

    [PrimaryKey] public string PublicKey{get;set;} = string.Empty;
    public string DisplayName{get;set;} = string.Empty;
    public string SharedAesKey{get;set;} = string.Empty;
    public string PigeonName{get;set;} = string.Empty;
    public DateTime CreatedAt{get;set;} = DateTime.UtcNow;
    [Ignore]
    public bool IsPublicBoard => PublicKey.StartsWith("PUBLICBOARD:");
    [Ignore]
    public bool IsPrivateBoard => PublicKey.StartsWith("PRIVATEBOARD:");
    [Ignore]
    public bool IsBoard => PublicKey.Contains("BOARD:");
    [Ignore]
    public bool IsDirectContact => !IsBoard;
    [Ignore]
    public string Icon => IsBoard ? "📢" : "👤";

    public Contact(){
        
    }

    public Contact(string Id, string name){
        DisplayName = name;
        PublicKey = Id;
    }

}