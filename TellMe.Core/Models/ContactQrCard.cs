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