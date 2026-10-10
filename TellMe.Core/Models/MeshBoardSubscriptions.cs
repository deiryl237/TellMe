namespace TellMe.Core.Models;

public class MeshBoardSubscriptions{

private string[] subscribed{get;set;}
private string[] unsubscribed{get;set;}

public string[] Subscribed{get => subscribed;
                            set => value = subscribed;}
public string[] Unsubscribed{get => unsubscribed;
                            set => value = unsubscribed;}

    public MeshBoardSubscriptions(string[] sub,string[] unsub){

    
}
}