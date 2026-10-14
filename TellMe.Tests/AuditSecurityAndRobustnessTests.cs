using System.Text;
using System.Text.Json;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Network;
using TellMe.Core.Security;
using TellMe.Core.Storage;

namespace TellMe.Tests;

/// <summary>

/// Ces tests vérifient les failles de sécurité, de robustesse réseau et de concurrence .
/// </summary>
public class AuditSecurityAndRobustnessTests
{
    // =========================================================================
    // USURPATION D'IDENTITÉ PAR MESSAGE NON SIGNÉ
    // =========================================================================
    public static string _dbPath = TellMeDbPath.dbpath();
    public TellMeDatabase db = new TellMeDatabase(_dbPath);
    public Account aliceAccount = new Account("Alice");
    public Account malloryAccount = new Account("Mallory");
    public Account bobAccount = new Account("Bob");

    public AuditSecurityAndRobustnessTests(){
        db.InitializeAsync();
        db.SaveAccountAsync(aliceAccount);
        db.SaveAccountAsync(malloryAccount);
        db.SaveAccountAsync(bobAccount);
    }
    [Fact]
    public void B1_AddMessage_UnsignedMessageClaimingPublicKey_MustBeRejected()
    {
        // Arrange : Alice a une vraie clé publique. Mallory usurpe Alice en envoyant Signature = ""
        var store = new MessageStore();
        var (alicePublic, _) = CryptoService.GenerateKeyPair();

        var fakeMessage = new MeshMessage(
            message: "Message usurpé",
            from: alicePublic,
            to: "Bob",
            parents: Array.Empty<string>()
             // Aucune signature !
        );

        // Act
        store.AddMessage(fakeMessage);

        // Assert : Le store ne doit JAMAIS accepter un message prétendant venir d'une clé publique sans signature
        Assert.Equal(0, store.Count);
        Assert.False(store.Contains(fakeMessage.Hash));
    }

    [Fact]
    public void B1_AddMessage_ForgedSignature_MustBeRejected()
    {
        // Arrange : Mallory signe un message avec sa propre clé privée en prétendant être Alice
        var store = new MessageStore();
        var (alicePublic, _) = CryptoService.GenerateKeyPair();
        var (_, malloryPrivate) = CryptoService.GenerateKeyPair();

        var message = new MeshMessage("Texte", alicePublic, "Bob", Array.Empty<string>());
        string forgedSig = CryptoService.Sign(message.Hash, malloryPrivate);

        var forgedMsg = new MeshMessage(
            message.Message,
            message.From,
            message.To,
            message.Parents,
            forgedSig
        );

        // Act
        store.AddMessage(forgedMsg);

        // Assert
        Assert.Equal(0, store.Count);
        Assert.False(store.Contains(forgedMsg.Hash));
    }


    // =========================================================================
    //ROBUSTESSE - ENTRÉES RÉSEAU MALFORMÉES & CRASH async void
    // =========================================================================

    [Fact]
    public async Task B2_SyncEngine_GarbageNonJsonData_MustNotThrowOrCrash()
    {
        // Arrange : Un pair hostile ou du bruit réseau envoie des octets qui ne sont pas du JSON
        var store = new MessageStore();
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var engine = new SyncEngine(store, bobTransport);

        byte[] garbage = Encoding.UTF8.GetBytes("Ceci n'est pas du JSON {corrompu!");

        // Act & Assert : L'envoi ne doit lever aucune exception non gérée dans async void
        var exception = await Record.ExceptionAsync(async () =>
        {
            await aliceTransport.SendAsync(garbage);
            await Task.Delay(50); // Laisse le temps au handler asynchrone de s'exécuter
        });

        Assert.Null(exception);
        Assert.Equal(0, store.Count); // Les données corrompues sont ignorées
    }

    [Fact]
    public async Task B2_SyncEngine_NullHeadsInPacket_MustNotThrowNullReference()
    {
        // Arrange : Un paquet malformé avec Type="Heads" mais Heads=null
        var store = new MessageStore();
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var engine = new SyncEngine(store, bobTransport);

        byte[] nullHeadsPacket = Encoding.UTF8.GetBytes("{\"Type\":\"Heads\",\"Heads\":null}");

        // Act & Assert : Ne doit pas lever de NullReferenceException sur packet.Heads
        var exception = await Record.ExceptionAsync(async () =>
        {
            await aliceTransport.SendAsync(nullHeadsPacket);
            await Task.Delay(50);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task B2_SyncEngine_MalformedPublicKeyInMessage_MustNotThrow()
    {
        // Arrange : Un message avec un champ From corrompu (pas du Base64 valide)
        var store = new MessageStore();
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var engine = new SyncEngine(store, bobTransport);

        var malformedMessage = new MeshMessage(
            message: "Hello",
            from: "PAS_UNE_CLE_BASE64_VALIDE!???",
            to: "Bob",
            parents: Array.Empty<string>()
        );

        var packet = new SyncPacket { Type = "Message", Message = malformedMessage };
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(packet);

        // Act & Assert : CryptoService.Verify ne doit pas lever FormatException ou CryptographicException
        var exception = await Record.ExceptionAsync(async () =>
        {
            await aliceTransport.SendAsync(data);
            await Task.Delay(50);
        });

        Assert.Null(exception);
        Assert.Equal(0, store.Count); // Message rejeté défensivement
    }


    // =========================================================================
    // STREAM FRAMING - DÉCOUPAGE DES PAQUETS RFCOMM COLLÉS DANS LE FLUX
    // =========================================================================

    [Fact]
    public async Task B3_Framing_GluedPacketsInSingleStream_BothMustBeProcessed()
    {
        // Arrange : Deux paquets valides collés dans un seul buffer reçu (phénomène standard en stream Bluetooth RFCOMM)
        var aliceStore = new MessageStore();
        var bobStore = new MessageStore();

        var (alicePublic, alicePrivate) = CryptoService.GenerateKeyPair();
        var m1 = MeshMessage.CreateSigned("Message 1", "Tous", Array.Empty<string>(), alicePublic, alicePrivate);
        var m2 = MeshMessage.CreateSigned("Message 2", "Tous", Array.Empty<string>(), alicePublic, alicePrivate);
        aliceStore.AddMessage(m1);
        aliceStore.AddMessage(m2);

        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var bobEngine = new SyncEngine(bobStore, bobTransport);

        // Deux paquets Message sérialisés et concaténés dans un seul tableau d'octets
        byte[] p1 = JsonSerializer.SerializeToUtf8Bytes(new SyncPacket { Type = "Message", Message = m1 });
        byte[] p2 = JsonSerializer.SerializeToUtf8Bytes(new SyncPacket { Type = "Message", Message = m2 });

        // Les deux paquets arrivent collés ensemble dans la même lecture socket : {...}{...}
        byte[] gluedBuffer = p1.Concat(p2).ToArray();

        // Act : Envoi du flux collé
        var exception = await Record.ExceptionAsync(async () =>
        {
            await aliceTransport.SendAsync(gluedBuffer);
            await Task.Delay(100);
        });

        // Assert : Aucun crash et les 2 messages doivent être extraits et traités par Bob !
        Assert.Null(exception);
        Assert.Equal(2, bobStore.Count);
        Assert.True(bobStore.Contains(m1.Hash));
        Assert.True(bobStore.Contains(m2.Hash));
    }


    // =========================================================================
    // CONCURRENCE - THREAD-SAFETY DANS MessageStore
    // =========================================================================

    [Fact]
    public async Task B4_MessageStore_ConcurrentAddAndEnumeration_MustNotThrow()
    {
        // Arrange : Plusieurs threads écrivent pendant que l'UI lit les messages en boucle
        var store = new MessageStore();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var (authorPublic, authorPrivate) = CryptoService.GenerateKeyPair();
        var writerTask = Task.Run(() =>
        {
            int counter = 0;
            while (!cts.Token.IsCancellationRequested && counter < 200)
            {
                try
                {
                    var msg = MeshMessage.CreateSigned($"Msg {counter}", "Tous", Array.Empty<string>(), authorPublic, authorPrivate);
                    store.AddMessage(msg);
                    counter++;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Tâche 2 : Lecture concurrente continue (comme le rafraîchissement UI / ViewModel)
        var readerTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    // L'énumération d'un Dictionary non thread-safe lève InvalidOperationException
                    foreach (var msg in store.Messages)
                    {
                        _ = msg.Hash;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        // Assert : Aucune InvalidOperationException ("Collection was modified")
        Assert.Empty(exceptions);
    }

    [Fact]
    public void B4_MessageStore_MessageAddedEvent_CurrentHeadsMustAlreadyBeUpdated()
    {
        // Arrange : Quand MessageAdded est déclenché, l'abonné (UI) doit voir les têtes à jour !
        var store = new MessageStore();
        string? observedHead = null;

        store.MessageAdded += msg =>
        {
            // L'UI observe les têtes dès la notification d'ajout
            observedHead = store.Heads.FirstOrDefault();
        };

        var (alicePublic, alicePrivate) = CryptoService.GenerateKeyPair();
        var m1 = MeshMessage.CreateSigned("Premier", "Tous", Array.Empty<string>(), alicePublic, alicePrivate);

        // Act
        store.AddMessage(m1);

        // Assert : La nouvelle tête DOIT être présente dès le déclenchement de l'événement !
        Assert.Equal(m1.Hash, observedHead);
    }

}
