using System.Security.Cryptography;
using System.Text;

namespace TellMe.Core.Security;

public static class CryptoService{

    public  static (string PublicKey, string PrivateKey) GenerateKeyPair(){

        using var  ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pubkey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        string privkey = Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
        return (pubkey,privkey);

    }

    public static string Sign(string data, string privateKey){

        using var ecdsa = ECDsa.Create();

        byte[] KeyBytes = Convert.FromBase64String(privateKey);
        ecdsa.ImportPkcs8PrivateKey(KeyBytes, out _);

        byte[] DataBytes = Encoding.UTF8.GetBytes(data);

        byte[] signatureBytes = ecdsa.SignData(DataBytes,HashAlgorithmName.SHA256);

        string signature = Convert.ToBase64String(signatureBytes);

        return signature;
    }

    public static bool Verify (string data, string signature, string publicKey){

        using var ecdsa = ECDsa.Create();

        byte[] KeyBytes = Convert.FromBase64String(publicKey);
        ecdsa.ImportSubjectPublicKeyInfo(KeyBytes, out _);

        byte[] dataBytes = Encoding.UTF8.GetBytes(data);

        byte[] signatureBytes = Convert.FromBase64String(signature);

        return ecdsa.VerifyData(dataBytes,signatureBytes,HashAlgorithmName.SHA256);

    }

    public static string Encrypt(string plainText, string key) {

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        using var aes = Aes.Create(); 
        aes.Key = keyBytes;

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = aes.EncryptCbc(plainBytes,aes.IV);
        byte[] result = aes.IV.Concat(cipherBytes).ToArray();

        return Convert.ToBase64String(result);

    }

    public static string Decrypt(string cipherText, string key){

        byte[] allBytes = Convert.FromBase64String(cipherText);
        byte[] IV = allBytes.Take(16).ToArray();
        byte[] cipherBytes = allBytes.Skip(16).ToArray();

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        byte[] result = aes.DecryptCbc(cipherBytes,IV);

        return Encoding.UTF8.GetString(result);

    }
}