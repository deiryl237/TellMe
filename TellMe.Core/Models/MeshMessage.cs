using System.Linq;
using System.Security.Cryptography;
using System.Text;


namespace Tellme.Core.Models;

public class MeshMessage {

public string Message{get;}
public string From{get;}
public string To{get;}
public string[] Parents {get;}
public string Hash {get;}


    public MeshMessage(string message,string from,string to,string[] parents){

         Message = message;
         From = from;
         To = to;
         Parents = parents != null ? parents.OrderBy(p => p).ToArray() : Array.Empty<string>();
         Hash = ComputerHash(Message,From,To,string.Join(",", Parents));
    }

    private string ComputerHash(string m,string f,string t, string p){
        string h = $"{m}|{f}|{t}|{p}" ;
        byte[] hEncoded = Encoding.UTF8.GetBytes(h);
        byte[] hashed = SHA256.HashData(hEncoded);
        string hFinal = Convert.ToHexString(hashed);
        return hFinal;
    }

}