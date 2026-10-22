using TellMe.Core.Models;
using TellMe.Core.Security;
using System.Security.Cryptography;
using System.Text;

namespace TellMe.Core.Network;

public class SyncPacket{

    public string Type{get;set;} = string.Empty;
    public string[]? Heads {get;set;}
    public string? RequestedHash {get;set;}
    public MeshMessage? Message{get;set;}
    public ContactRequest? request{get;set;}
    public ContactResponse? response{get;set;}

}

public class ContactRequest{
    public string RequesterId{get;set;} = string.Empty;
    public string TargetId{get;set;} = string.Empty;
    public string SharedAesKey{get;set;} = string.Empty;
    public string Name{get;set;} = string.Empty;
    public string Pigeon{get;set;} = string.Empty;
    public string RequestSignature{get;set;} = string.Empty;
    public string RequestHash{get;set;} = string.Empty;

    public ContactRequest(){

    }

    public ContactRequest(Account requesterAccount, string targetId = ""){
        SharedAesKey = CryptoService.GenerateAesKeys();

        RequesterId = requesterAccount.Id;
        TargetId = targetId;
        Name = requesterAccount.DisplayName;
        Pigeon = requesterAccount.PigeonName;

        RequestHash = ComputerHash(RequesterId,SharedAesKey,Name,Pigeon,TargetId);
        RequestSignature = CryptoService.Sign(RequestHash,requesterAccount.PrivateKey);
    }

    private string ComputerHash(string R,string S,string N, string P, string T = ""){
        string h = string.IsNullOrEmpty(T) ? $"{R}|{S}|{N}|{P}" : $"{R}|{S}|{N}|{P}|{T}";
        byte[] hEncoded = Encoding.UTF8.GetBytes(h);
        byte[] hashed = SHA256.HashData(hEncoded);
        string hFinal = Convert.ToHexString(hashed);
        return hFinal;
    }

}

public class ContactResponse{
    public bool ContactAccepted{get;set;} = false;
    public string ResponseSignature{get;set;} = string.Empty;

    public ContactResponse(){

    }

    public ContactResponse(string RequestHash,bool Accepted, Account account){
        ContactAccepted = Accepted;
        ResponseSignature = CryptoService.Sign($"{RequestHash}|{(ContactAccepted ? "accepted" : "rejected")}",account.PrivateKey);
    }
}
