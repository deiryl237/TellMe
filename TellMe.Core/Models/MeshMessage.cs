using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TellMe.Core.Security;


namespace TellMe.Core.Models;

public class MeshMessage {

public string Message{get;}
public string From{get;}
public string To{get;}
public string[] Parents {get;}
public string Hash {get;}
public string Signature { get;set; } = string.Empty;



    public MeshMessage(string message,string from,string to,string[] parents,string signature = ""){

         Message = message;
         From = from;
         To = to;
         Parents = parents != null ? parents.OrderBy(p => p).ToArray() : Array.Empty<string>();
         Hash = ComputerHash(Message,From,To,string.Join(",", Parents));
         Signature = signature;

    }

    private string ComputerHash(string m,string f,string t, string p){
        string h = $"{m}|{f}|{t}|{p}" ;
        byte[] hEncoded = Encoding.UTF8.GetBytes(h);
        byte[] hashed = SHA256.HashData(hEncoded);
        string hFinal = Convert.ToHexString(hashed);
        return hFinal;
    }

    public static MeshMessage CreateSigned(string message, string to, string[] parents,string publicKey, string privateKey){

        MeshMessage msg = new MeshMessage(message,publicKey,to,parents);
        string sign = CryptoService.Sign(msg.Hash,privateKey);
        msg.Signature = sign ;

        return msg;

    }

}