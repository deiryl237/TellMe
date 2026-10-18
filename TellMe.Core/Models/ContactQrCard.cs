using System.Text.Json;

namespace TellMe.Core.Models;

public class ContactQrCard {
    public string id {get;set;} = string.Empty;
    public string DisplayName {get;set;} = string.Empty;
    public string PigeonName {get;set;} = string.Empty;
    public string BluetoothAddress {get;set;} = string.Empty;


    public ContactQrCard(){
    }
    public ContactQrCard(Account account,string bluetoothAddress){
    
        id = account.Id;
        DisplayName = account.DisplayName;
        PigeonName = account.PigeonName;
        BluetoothAddress = bluetoothAddress;

    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ContactQrCard? Fromjson(string json){

        var contactcard = JsonSerializer.Deserialize<ContactQrCard>(json);
        return contactcard;

    }
}

public class BoardQrCard {
    public string Id {get;set;} = string.Empty;
    public string DisplayName {get;set;} = string.Empty;
    public string key{get;set;} = string.Empty;

    public BoardQrCard(string id,string name,string _key){

        key = _key;
        DisplayName = name;
        Id = id;

    }

    public string ToJson() => JsonSerializer.Serialize(this);
    public static BoardQrCard? Fromjson(string json){

        var boardcard = JsonSerializer.Deserialize<BoardQrCard>(json);
        return boardcard;

    }
}