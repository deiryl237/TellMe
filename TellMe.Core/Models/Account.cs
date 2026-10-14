using SQLite;
using TellMe.Core.Security;
namespace TellMe.Core.Models;


public class Account{

public string DisplayName{get;set;} = string.Empty;
[PrimaryKey]public string Id{get;set;} = string.Empty;
public string PrivateKey {get;set;} = string.Empty;
public DateTime CreatedAt {get;set;} = DateTime.UtcNow;
public string PigeonName {get;set;} = "Pigeon";
public string FeatherColor {get;set;} = "Bleu";
public string Accessory {get;set;} = "Aucun";
public int ExperiencePoints {get;set;} = 0;

    public Account(){

    }

    public Account(string name) : this() {
        DisplayName = name;
        var (pubKey,privKey) = CryptoService.GenerateKeyPair();
        Id = pubKey;
        PrivateKey = privKey;
    }


}