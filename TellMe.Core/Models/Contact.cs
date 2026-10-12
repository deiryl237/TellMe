using SQLite;

public class Contact{

    [PrimaryKey] public string PublicKey{get;set;} = string.Empty;
    public string DisplayName{get;set;} = string.Empty;
    public string SharedAesKey{get;set;} = string.Empty;
    public string PigeonName{get;set;} = string.Empty;
    public DateTime CreatedAt{get;set;} = DateTime.UtcNow;

    public Contact(){
        
    }

}