using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Security;

namespace TellMe.Tests;

public class CryptoTests
{
    [Fact]
    public void GenerateKeyPair_ProducesValidDistinctKeys()
    {
        // 1. Act : Générer une paire de clés pour un nouvel utilisateur
        var (publicKey, privateKey) = CryptoService.GenerateKeyPair();

        // 2. Assert : Les clés doivent être non nulles, non vides et distinctes
        Assert.False(string.IsNullOrWhiteSpace(publicKey));
        Assert.False(string.IsNullOrWhiteSpace(privateKey));
        Assert.NotEqual(publicKey, privateKey);
    }

    [Fact]
    public void SignAndVerify_ValidDataAndKey_ReturnsTrue()
    {
        // 1. Arrange
        var (publicKey, privateKey) = CryptoService.GenerateKeyPair();
        string dataToSign = "3f82a91b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f"; // Ex: Hash de message

        // 2. Act : Alice signe les données avec sa clé privée
        string signature = CryptoService.Sign(dataToSign, privateKey);

        // 3. Assert : N'importe qui peut vérifier avec la clé publique d'Alice
        bool isValid = CryptoService.Verify(dataToSign, signature, publicKey);
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_TamperedData_ReturnsFalse()
    {
        // 1. Arrange
        var (publicKey, privateKey) = CryptoService.GenerateKeyPair();
        string originalData = "HashOriginal_12345";
        string tamperedData = "HashModifie_99999"; // Un attaquant tente de modifier le message

        // 2. Act : Signature des données d'origine
        string signature = CryptoService.Sign(originalData, privateKey);

        // 3. Assert : La vérification sur les données altérées doit échouer !
        bool isValid = CryptoService.Verify(tamperedData, signature, publicKey);
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_WrongPublicKey_ReturnsFalse()
    {
        // 1. Arrange
        var (alicePublic, alicePrivate) = CryptoService.GenerateKeyPair();
        var (bobPublic, _) = CryptoService.GenerateKeyPair();
        string messageHash = "HashMessage_Valid";

        // 2. Act : Alice signe le message
        string signature = CryptoService.Sign(messageHash, alicePrivate);

        // 3. Assert : Si on prétend que le message vient de Bob, la vérification échoue
        bool isValid = CryptoService.Verify(messageHash, signature, bobPublic);
        Assert.False(isValid);
    }

    [Fact]
    public void AddMessage_WithForgedSignature_IsRejectedByStore()
    {
        // 1. Arrange : Alice crée un message, mais Mallory le signe avec une fausse clé
        var store = new MessageStore();
        var (alicePublic, _) = CryptoService.GenerateKeyPair();
        var (_, malloryPrivate) = CryptoService.GenerateKeyPair();

        var message = new MeshMessage("Texte important", alicePublic, "Bob", Array.Empty<string>());
        // Mallory signe le hash avec sa propre clé privée au lieu de celle d'Alice !
        string forgedSignature = CryptoService.Sign(message.Hash, malloryPrivate);

        var forgedMessage = new MeshMessage(
            message.Message, 
            message.From, 
            message.To, 
            message.Parents, 
            forgedSignature
        );

        // 2. Act : Tentative d'insertion du message falsifié
        store.AddMessage(forgedMessage);

        // 3. Assert : Le store a détecté l'imposture et a rejeté net le message !
        Assert.Equal(0, store.Count);
        Assert.False(store.Contains(forgedMessage.Hash));
    }

    [Fact]
    public void AddMessage_WithAuthenticSignature_IsAcceptedByStore()
    {
        // 1. Arrange : Alice signe authentiquement son message
        var store = new MessageStore();
        var (alicePublic, alicePrivate) = CryptoService.GenerateKeyPair();

        var message = new MeshMessage("Texte authentique", alicePublic, "Bob", Array.Empty<string>());
        string authenticSignature = CryptoService.Sign(message.Hash, alicePrivate);

        var signedMessage = new MeshMessage(
            message.Message, 
            message.From, 
            message.To, 
            message.Parents, 
            authenticSignature
        );

        // 2. Act
        store.AddMessage(signedMessage);

        // 3. Assert
        Assert.Equal(1, store.Count);
        Assert.True(store.Contains(signedMessage.Hash));
    }

    [Fact]
    public void EncryptAndDecrypt_RestoresOriginalMessage()
    {
        // 1. Arrange
        string plainText = "Rendez-vous secret sous le grand chêne à 18h";
        string secretKey = "UneCleSecreteTresSolidePourAES25"; // 32 caractères = 256 bits

        // 2. Act : Chiffrement puis déchiffrement
        string cipherText = CryptoService.Encrypt(plainText, secretKey);
        string decryptedText = CryptoService.Decrypt(cipherText, secretKey);

        // 3. Assert : Le texte chiffré est illisible et le texte déchiffré est identique à l'original
        Assert.NotEqual(plainText, cipherText);
        Assert.Equal(plainText, decryptedText);
    }
}

