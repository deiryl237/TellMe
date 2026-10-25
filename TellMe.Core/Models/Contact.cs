using SQLite;

namespace TellMe.Core.Models;

public class Contact{

    [PrimaryKey] public string PublicKey{get;set;} = string.Empty;
    public string DisplayName{get;set;} = string.Empty;
    public string SharedAesKey{get;set;} = string.Empty;
    public string PigeonName{get;set;} = string.Empty;
    public string PigeonState { get; set; } = "AtPerch"; // "AtPerch" | "InFlight"
    public DateTime? FlightStartedAt { get; set; }
    public int MessagesInFlightCount { get; set; } = 0;
    public string PigeonAccessory { get; set; } = "Aucun";
    public string PigeonFeatherColor { get; set; } = "Bleu";
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
    public string Icon => IsBoard ? "icon_broadcast.svg" : "icon_user.svg";
    [Ignore]
    public string ShortId => IsBoard
        ? PublicKey
        : (PublicKey.Length > 16 ? $"ID: #{PublicKey[^8..]}" : PublicKey);

    public Contact(){
        
    }

    public Contact(string Id, string name){
        DisplayName = name;
        PublicKey = Id;
    }

}