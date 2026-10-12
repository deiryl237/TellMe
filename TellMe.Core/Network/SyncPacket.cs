using TellMe.Core.Models;

namespace TellMe.Core.Network;

public class SyncPacket{

    public string Type{get;set;} = string.Empty;
    public string[]? Heads {get;set;}
    public string? RequestedHash {get;set;}
    public MeshMessage? Message{get;set;}

}

