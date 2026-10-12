using System.Security.Cryptography;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Security;
using TellMe.Core.Storage;

namespace TellMe.Tests;

/// <summary>
/// Grand test d'intégration de bout en bout simulant une mission complète
/// de messagerie décentralisée (Pigeon Voyageur DTN) :
/// - Identités et profils avec SQLite
/// - Chiffrement symétrique E2EE (AES-256)
/// - Signatures asymétriques (ECDSA P-256)
/// - Routage opportuniste multi-sauts via un tiers relais (Charlie)
/// - Causalité du DAG de Merkle (Genesis, réponse et convergence des Heads)
/// </summary>
public class GrandScenarioEndToEndTests : IDisposable
{
    private readonly string _aliceDbPath;
    private readonly string _bobDbPath;
    private readonly string _charlieDbPath;

    public GrandScenarioEndToEndTests()
    {
        _aliceDbPath = Path.Combine(Path.GetTempPath(), $"tellme_alice_{Guid.NewGuid()}.db3");
        _bobDbPath = Path.Combine(Path.GetTempPath(), $"tellme_bob_{Guid.NewGuid()}.db3");
        _charlieDbPath = Path.Combine(Path.GetTempPath(), $"tellme_charlie_{Guid.NewGuid()}.db3");
    }

    public void Dispose()
    {
        foreach (var path in new[] { _aliceDbPath, _bobDbPath, _charlieDbPath })
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { /* Ignore verrous temporaires */ }
            }
        }
    }

    /// <summary>
    /// Synchronisation bidirectionnelle lors d'une rencontre Bluetooth entre deux nœuds.
    /// </summary>
    private static void Sync(MessageStore a, MessageStore b)
    {
        SyncOneWay(a, b);
        SyncOneWay(b, a);
    }

    /// <summary>
    /// Protocole Gossip Merkle Pull : transfert des têtes inconnues et résolution des orphelins.
    /// </summary>
    private static void SyncOneWay(MessageStore source, MessageStore target)
    {
        foreach (string head in source.Heads)
        {
            if (!target.Contains(head))
            {
                var msg = source.GetMessage(head);
                if (msg != null)
                {
                    target.AddMessage(msg);
                }
            }
        }

        while (target.BufferCount > 0)
        {
            bool hasProgressed = false;
            foreach (string missingParent in target.MissingParents.ToList())
            {
                if (source.Contains(missingParent))
                {
                    var parentMsg = source.GetMessage(missingParent);
                    if (parentMsg != null)
                    {
                        target.AddMessage(parentMsg);
                        hasProgressed = true;
                    }
                }
            }

            if (!hasProgressed)
            {
                break;
            }
        }
    }

    [Fact]
    public async Task GrandScenario_PigeonCarrier_FromAliceToBobViaCharlie_WithE2EE_And_Signatures()
    {
        // =========================================================================
        // ACTE 1 : Création des identités, profils et bases SQLite locales
        // =========================================================================

        // 1.1 Alice génère ses clés et son compte
        var (alicePublic, alicePrivate) = CryptoService.GenerateKeyPair();
        var aliceAccount = new Account("Alice")
        {
            Id = alicePublic,
            PigeonName = "Rapide",
            FeatherColor = "Gris Perle",
            Accessory = "Bague Argent"
        };
        var aliceDb = new TellMeDatabase(_aliceDbPath);
        await aliceDb.InitializeAsync();
        await aliceDb.SaveAccountAsync(aliceAccount);
        var aliceStore = new MessageStore();

        // 1.2 Bob génère ses clés et son compte
        var (bobPublic, bobPrivate) = CryptoService.GenerateKeyPair();
        var bobAccount = new Account("Bob")
        {
            Id = bobPublic,
            PigeonName = "Fidèle",
            FeatherColor = "Blanc Royal",
            Accessory = "Petite Écharpe"
        };
        var bobDb = new TellMeDatabase(_bobDbPath);
        await bobDb.InitializeAsync();
        await bobDb.SaveAccountAsync(bobAccount);
        var bobStore = new MessageStore();

        // 1.3 Charlie (le transporteur / pigeon relais)
        var (charliePublic, _) = CryptoService.GenerateKeyPair();
        var charlieAccount = new Account("Charlie")
        {
            Id = charliePublic,
            PigeonName = "Voyageur Solitaire"
        };
        var charlieDb = new TellMeDatabase(_charlieDbPath);
        await charlieDb.InitializeAsync();
        await charlieDb.SaveAccountAsync(charlieAccount);
        var charlieStore = new MessageStore();

        // 1.4 Clé secrète E2EE partagée entre Alice et Bob (32 caractères = 256 bits)
        string sharedSecretKey = "CleSecretePriveePourAliceEtBob32";

        // =========================================================================
        // ACTE 2 : Alice rédige, chiffre, signe et stocke son message
        // =========================================================================

        string plainTextAlice = "Rendez-vous sous le vieux chêne à 22h avec la lampe.";

        // 2.1 Chiffrement de bout en bout du contenu
        string cipherTextAlice = CryptoService.Encrypt(plainTextAlice, sharedSecretKey);
        Assert.NotEqual(plainTextAlice, cipherTextAlice);

        // 2.2 Création du message non signé
        var draftAliceMsg = new MeshMessage(
            cipherTextAlice,
            from: alicePublic,
            to: bobPublic,
            parents: Array.Empty<string>() // Message Genesis de la conversation
        );

        // 2.3 Signature numérique par la clé privée d'Alice
        string signatureAlice = CryptoService.Sign(draftAliceMsg.Hash, alicePrivate);
        var signedAliceMsg = new MeshMessage(
            draftAliceMsg.Message,
            draftAliceMsg.From,
            draftAliceMsg.To,
            draftAliceMsg.Parents,
            signatureAlice
        );

        // 2.4 Insertion dans le MessageStore et la base SQLite d'Alice
        aliceStore.AddMessage(signedAliceMsg);
        await aliceDb.SaveMessageAsync(signedAliceMsg, isPersonal: true, isOrphan: false);

        Assert.Equal(1, aliceStore.Count);
        Assert.True(aliceStore.Contains(signedAliceMsg.Hash));
        var alicePersisted = await aliceDb.GetAllMessagesAsync();
        Assert.Single(alicePersisted);
        Assert.Equal(signedAliceMsg.Signature, alicePersisted[0].Signature);

        // =========================================================================
        // ACTE 3 : Rencontre 1 — Alice croise Charlie (Le pigeon relais prend la lettre)
        // =========================================================================

        // 3.1 Sync locale Bluetooth Alice <-> Charlie
        Sync(aliceStore, charlieStore);

        // 3.2 Charlie intègre le message dans son store et sa base (isPersonal = false)
        Assert.True(charlieStore.Contains(signedAliceMsg.Hash));
        await charlieDb.SaveMessageAsync(signedAliceMsg, isPersonal: false, isOrphan: false);

        // 3.3 Garantie de confidentialité : Charlie ne peut pas déchiffrer le message
        Assert.ThrowsAny<CryptographicException>(() =>
        {
            CryptoService.Decrypt(signedAliceMsg.Message, "CleErronneeDeCharlieNonValide123");
        });

        // =========================================================================
        // ACTE 4 : Tentative d'attaque par Mallory (Altération / Usurpation)
        // =========================================================================

        var (malloryPublic, malloryPrivate) = CryptoService.GenerateKeyPair();
        var maliciousMsg = new MeshMessage(
            "Rejoins-moi au piège de la falaise",
            from: alicePublic, // Mallory prétend être Alice !
            to: bobPublic,
            parents: Array.Empty<string>()
        );
        // Mallory signe avec sa propre clé privée car il ne possède pas celle d'Alice
        string forgedSignature = CryptoService.Sign(maliciousMsg.Hash, malloryPrivate);
        var attackMsg = new MeshMessage(
            maliciousMsg.Message,
            maliciousMsg.From,
            maliciousMsg.To,
            maliciousMsg.Parents,
            forgedSignature
        );

        // Charlie et Bob rejettent net la tentative d'imposture !
        charlieStore.AddMessage(attackMsg);
        Assert.False(charlieStore.Contains(attackMsg.Hash));

        // =========================================================================
        // ACTE 5 : Rencontre 2 — Charlie arrive chez Bob (Livraison & Déchiffrement)
        // =========================================================================

        // 5.1 Sync locale Bluetooth Charlie <-> Bob (Alice est hors de portée)
        Sync(charlieStore, bobStore);

        // 5.2 Bob a bien reçu le message d'Alice
        Assert.True(bobStore.Contains(signedAliceMsg.Hash));
        await bobDb.SaveMessageAsync(signedAliceMsg, isPersonal: true, isOrphan: false);

        // 5.3 Bob vérifie l'authenticité de la signature
        bool isAliceSignatureValid = CryptoService.Verify(
            signedAliceMsg.Hash,
            signedAliceMsg.Signature,
            alicePublic
        );
        Assert.True(isAliceSignatureValid);

        // 5.4 Bob déchiffre le message avec la clé partagée
        string decryptedAliceMsg = CryptoService.Decrypt(signedAliceMsg.Message, sharedSecretKey);
        Assert.Equal(plainTextAlice, decryptedAliceMsg);

        // =========================================================================
        // ACTE 6 : Réponse Causale de Bob (Ack & Suite du dialogue)
        // =========================================================================

        string plainTextBob = "Bien reçu Alice ! J'y serai à l'heure avec le matériel.";
        string cipherTextBob = CryptoService.Encrypt(plainTextBob, sharedSecretKey);

        // Le message d'Alice devient le PARENT direct du message de Bob dans le DAG
        var draftBobMsg = new MeshMessage(
            cipherTextBob,
            from: bobPublic,
            to: alicePublic,
            parents: new[] { signedAliceMsg.Hash }
        );

        string signatureBob = CryptoService.Sign(draftBobMsg.Hash, bobPrivate);
        var signedBobMsg = new MeshMessage(
            draftBobMsg.Message,
            draftBobMsg.From,
            draftBobMsg.To,
            draftBobMsg.Parents,
            signatureBob
        );

        bobStore.AddMessage(signedBobMsg);
        await bobDb.SaveMessageAsync(signedBobMsg, isPersonal: true, isOrphan: false);

        // Le graphe de Bob a maintenant 2 messages et 1 seule tête (le message le plus récent de Bob)
        Assert.Equal(2, bobStore.Count);
        Assert.Single(bobStore.Heads);
        Assert.Contains(signedBobMsg.Hash, bobStore.Heads);

        // =========================================================================
        // ACTE 7 : Voyage Retour — Bob transmet à Charlie, qui ramène la réponse à Alice
        // =========================================================================

        // 7.1 Bob recroise Charlie le lendemain
        Sync(bobStore, charlieStore);
        Assert.True(charlieStore.Contains(signedBobMsg.Hash));

        // 7.2 Charlie recroise Alice plus tard
        Sync(charlieStore, aliceStore);
        Assert.True(aliceStore.Contains(signedBobMsg.Hash));

        // 7.3 Alice récupère et valide la réponse de Bob
        var receivedReply = aliceStore.GetMessage(signedBobMsg.Hash);
        Assert.NotNull(receivedReply);

        bool isBobSignatureValid = CryptoService.Verify(
            receivedReply.Hash,
            receivedReply.Signature,
            bobPublic
        );
        Assert.True(isBobSignatureValid);

        string decryptedBobMsg = CryptoService.Decrypt(receivedReply.Message, sharedSecretKey);
        Assert.Equal(plainTextBob, decryptedBobMsg);

        // 7.4 Convergence causale totale : Alice a désormais exactement les mêmes 2 messages et la même tête !
        Assert.Equal(2, aliceStore.Count);
        Assert.Single(aliceStore.Heads);
        Assert.Contains(signedBobMsg.Hash, aliceStore.Heads);
        Assert.True(aliceStore.Contains(signedAliceMsg.Hash));
        Assert.True(aliceStore.Contains(signedBobMsg.Hash));
    }
}
