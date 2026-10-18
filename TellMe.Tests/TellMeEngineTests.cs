using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Storage;
using TellMe.Core.Network;
using TellMe.Core.Security;

namespace TellMe.Tests;

public class TellMeEngineTests
{
    // =========================================================================
    // 1. Tests d'initialisation et de compte
    // =========================================================================

    [Fact]
    public async Task InitializeAsync_NewDatabase_CreatesAndPersistsAccount()
    {
        string dbPath = TellMeDbPath.dbpath();
        var database = new TellMeDatabase(dbPath);
        var transport = new VirtualTransport();
        var engine = new TellMeEngine(database, transport);

        await engine.InitializeAsync("Alice");

        Assert.NotNull(engine._myaccount);
        Assert.Equal("Alice", engine._myaccount.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(engine._myaccount.Id));

        var dbAccount = await database.GetAccountAsync();
        Assert.NotNull(dbAccount);
        Assert.Equal("Alice", dbAccount.DisplayName);
        Assert.Equal(engine._myaccount.Id, dbAccount.Id);
    }

    [Fact]
    public async Task InitializeAsync_ExistingDatabase_LoadsExistingAccount()
    {
        string dbPath = TellMeDbPath.dbpath();
        var database = new TellMeDatabase(dbPath);
        var transport1 = new VirtualTransport();
        var engine1 = new TellMeEngine(database, transport1);
        await engine1.InitializeAsync("Alice");
        string originalAliceId = engine1._myaccount!.Id;

        var transport2 = new VirtualTransport();
        var engine2 = new TellMeEngine(database, transport2);
        await engine2.InitializeAsync("Bob"); // Doit recharger le compte existant (Alice)

        Assert.NotNull(engine2._myaccount);
        Assert.Equal("Alice", engine2._myaccount.DisplayName);
        Assert.Equal(originalAliceId, engine2._myaccount.Id);
    }

    // =========================================================================
    // 2. Tests d'envoi et de déchiffrement contextuel (Public vs Direct vs Private)
    // =========================================================================

    [Fact]
    public async Task SendMessageAsync_ToPublicBoard_StoresPlainTextMessage()
    {
        // 1. Arrange : Alice sur son engine
        string dbPath = TellMeDbPath.dbpath();
        var database = new TellMeDatabase(dbPath);
        var engine = new TellMeEngine(database, new VirtualTransport());
        await engine.InitializeAsync("Alice");

        // 2. Act : Alice envoie un message sur un board public
        string publicBoardId = "PUBLICBOARD:Campus";
        string content = "Bienvenue à tous sur le tableau public du campus !";
        var msg = await engine.SendMessageAsync(content, publicBoardId);

        // 3. Assert : Message en clair, non chiffré
        Assert.Equal(publicBoardId, msg.To);
        Assert.Equal(engine._myaccount!.Id, msg.From);
        Assert.Equal(content, msg.Message); // Le texte brut reste en clair dans le payload

        // Vérification dans le DAG et dans SQLite
        Assert.True(engine._store.Contains(msg.Hash));
        var stored = await database.GetAllStoredMessagesAsync();
        Assert.Contains(stored, s => s.Hash == msg.Hash);

        // La lecture via GetDecryptedContentAsync renvoie le texte d'origine
        string readable = await engine.GetDecryptedContentAsync(msg);
        Assert.Equal(content, readable);
    }

    [Fact]
    public async Task SendMessageAsync_ToDirectContact_EncryptsPayloadWithSharedAesKey()
    {
        // 1. Arrange : Alice a Bob enregistré comme contact avec une clé AES partagée
        string dbPath = TellMeDbPath.dbpath();
        var database = new TellMeDatabase(dbPath);
        var engine = new TellMeEngine(database, new VirtualTransport());
        await engine.InitializeAsync("Alice");

        string sharedAesKey = CryptoService.GenerateAesKeys();
        var bobContact = new Contact
        {
            PublicKey = "BOB_PUBLIC_KEY_123456789",
            DisplayName = "Bob",
            PigeonName = "Hedwige",
            SharedAesKey = sharedAesKey
        };
        await database.SaveContactAsync(bobContact);

        // 2. Act : Alice envoie un message privé à Bob
        string secretContent = "Rendez-vous secret sous le vieux chêne à 22h";
        var msg = await engine.SendMessageAsync(secretContent, bobContact.PublicKey);

        // 3. Assert : Le payload transitant sur le réseau est chiffré !
        Assert.NotEqual(secretContent, msg.Message);

        // Le déchiffrement par Alice (ou par Bob qui a la même clé) restitue le message en clair
        string readable = await engine.GetDecryptedContentAsync(msg);
        Assert.Equal(secretContent, readable);
    }

    [Fact]
    public async Task SendMessageAsync_ToPrivateBoard_EncryptsWithBoardAesKey()
    {
        // 1. Arrange : Un board privé avec sa propre clé AES de groupe
        string dbPath = TellMeDbPath.dbpath();
        var database = new TellMeDatabase(dbPath);
        var engine = new TellMeEngine(database, new VirtualTransport());
        await engine.InitializeAsync("Alice");

        string boardAesKey = CryptoService.GenerateAesKeys();
        string privateBoardId = "PRIVATEBOARD:SecretSociety";
        var privateBoardContact = new Contact
        {
            PublicKey = privateBoardId,
            DisplayName = "Société Secrète",
            PigeonName = "Corbeau",
            SharedAesKey = boardAesKey
        };
        await database.SaveContactAsync(privateBoardContact);

        // 2. Act : Alice poste sur le board privé
        string confidentialPost = "Mot de passe du jour : PHOENIX";
        var msg = await engine.SendMessageAsync(confidentialPost, privateBoardId);

        // 3. Assert : Chiffré dans le DAG et la base
        Assert.NotEqual(confidentialPost, msg.Message);

        // Déchiffrable par n'importe quel membre possédant le contact du board privé
        string readable = await engine.GetDecryptedContentAsync(msg);
        Assert.Equal(confidentialPost, readable);
    }

    // =========================================================================
    // 3. Test de Handshake de Contact de bout en bout entre deux TellMeEngines
    // =========================================================================

    [Fact]
    public async Task ContactHandshake_BetweenTwoEngines_ExchangesKeysAndEnablesEncryptedChat()
    {
        // 1. Arrange : Alice et Bob ont chacun leur TellMeEngine reliés par VirtualTransport
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var aliceDb = new TellMeDatabase(TellMeDbPath.dbpath());
        var bobDb = new TellMeDatabase(TellMeDbPath.dbpath());

        var aliceEngine = new TellMeEngine(aliceDb, aliceTransport);
        var bobEngine = new TellMeEngine(bobDb, bobTransport);

        await aliceEngine.InitializeAsync("Alice");
        await bobEngine.InitializeAsync("Bob");

        // Bob prépare sa carte QR
        var bobCard = new ContactQrCard
        {
            id = bobEngine._myaccount!.Id,
            DisplayName = bobEngine._myaccount.DisplayName,
            PigeonName = bobEngine._myaccount.PigeonName,
            BluetoothAddress = "00:11:22:33:44:55"
        };

        // Bob configure son approbation automatique pour la demande d'Alice
        bobEngine._syncEngine.ContactRequestReceived += async (req) =>
        {
            await bobEngine.AcceptContactRequestAsync(req);
        };

        // 2. Act : Alice scanne la carte de Bob et initie l'ajout de contact
        await aliceEngine.AddContactFromScannedCardAsync(bobCard);

        // Laisser le temps à la demande et à la réponse de circuler
        for (int i = 0; i < 30; i++)
        {
            var aliceBobContact = await aliceDb.GetContactAsync(bobCard.id);
            var bobAliceContact = await bobDb.GetContactAsync(aliceEngine._myaccount.Id);
            if (aliceBobContact != null && bobAliceContact != null)
                break;
            await Task.Delay(30);
        }

        // 3. Assert : Les deux téléphones ont enregistré mutuellement leur contact
        var aliceSavedBob = await aliceDb.GetContactAsync(bobCard.id);
        var bobSavedAlice = await bobDb.GetContactAsync(aliceEngine._myaccount.Id);

        Assert.NotNull(aliceSavedBob);
        Assert.NotNull(bobSavedAlice);
        Assert.Equal(aliceSavedBob.SharedAesKey, bobSavedAlice.SharedAesKey); // Même clé AES partagée !

        // 4. Preuve ultime : Alice envoie un message chiffré à Bob, Bob le reçoit et le déchiffre !
        var secretMsg = await aliceEngine.SendMessageAsync("Félicitations pour le handshake !", bobCard.id);

        // Bob synchronise le message
        for (int i = 0; i < 30 && !bobEngine._store.Contains(secretMsg.Hash); i++)
        {
            await Task.Delay(30);
        }

        Assert.True(bobEngine._store.Contains(secretMsg.Hash));
        string bobRead = await bobEngine.GetDecryptedContentAsync(secretMsg);
        Assert.Equal("Félicitations pour le handshake !", bobRead);
    }
}
