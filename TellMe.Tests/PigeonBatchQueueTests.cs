using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Storage;
using TellMe.Core.Network;
using TellMe.Core.Security;
using Xunit;

namespace TellMe.Tests;

public class PigeonBatchQueueTests
{
    private async Task<(TellMeEngine Alice, TellMeEngine Bob, Contact BobContactOnAlice, Contact AliceContactOnBob)> SetupPairedEnginesAsync()
    {
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var aliceDb = new TellMeDatabase(TellMeDbPath.dbpath());
        var bobDb = new TellMeDatabase(TellMeDbPath.dbpath());

        var aliceEngine = new TellMeEngine(aliceDb, aliceTransport);
        var bobEngine = new TellMeEngine(bobDb, bobTransport);

        await aliceEngine.InitializeAsync("Alice");
        await bobEngine.InitializeAsync("Bob");

        string sharedKey = CryptoService.GenerateAesKeys();

        var bobContact = new Contact
        {
            PublicKey = bobEngine._myaccount!.Id,
            DisplayName = "Bob",
            PigeonName = "Gilbert",
            SharedAesKey = sharedKey,
            PigeonState = "AtPerch"
        };
        await aliceDb.SaveContactAsync(bobContact);

        var aliceContact = new Contact
        {
            PublicKey = aliceEngine._myaccount!.Id,
            DisplayName = "Alice",
            PigeonName = "Barnabé",
            SharedAesKey = sharedKey,
            PigeonState = "AtPerch"
        };
        await bobDb.SaveContactAsync(aliceContact);

        return (aliceEngine, bobEngine, bobContact, aliceContact);
    }

    [Fact]
    public async Task SendMessage_WhenPigeonAtPerch_TakesFlightAndGetsDeliveredByAck()
    {
        var (alice, bob, bobContact, _) = await SetupPairedEnginesAsync();

        // 1. Avant l'envoi : Pigeon au perchoir
        var contactBefore = await alice._database.GetContactAsync(bobContact.PublicKey);
        Assert.NotNull(contactBefore);
        Assert.Equal("AtPerch", contactBefore.PigeonState);

        // 2. Alice envoie un message M1 à Bob
        var m1 = await alice.SendMessageAsync("Bonjour Bob !", bobContact.PublicKey);

        // Vérification immédiate chez Alice : le pigeon s'est envolé
        var contactInFlight = await alice._database.GetContactAsync(bobContact.PublicKey);
        Assert.NotNull(contactInFlight);
        Assert.Equal("InFlight", contactInFlight.PigeonState);
        Assert.Equal(1, contactInFlight.MessagesInFlightCount);

        var storedM1 = (await alice._database.GetAllStoredMessagesAsync()).FirstOrDefault(m => m.Hash == m1.Hash);
        Assert.NotNull(storedM1);
        Assert.Equal(MessageDeliveryStatus.InFlight, storedM1.DeliveryStatus);

        // 3. Attendre la synchronisation et la réception de l'ACK automatique par Alice
        for (int i = 0; i < 40; i++)
        {
            var updatedM1 = (await alice._database.GetAllStoredMessagesAsync()).FirstOrDefault(m => m.Hash == m1.Hash);
            var updatedContact = await alice._database.GetContactAsync(bobContact.PublicKey);
            if (updatedM1?.DeliveryStatus == MessageDeliveryStatus.Delivered && updatedContact?.PigeonState == "AtPerch")
            {
                break;
            }
            await Task.Delay(30);
        }

        // 4. Assert final : M1 est Delivered et le pigeon est rentré au perchoir
        var finalM1 = (await alice._database.GetAllStoredMessagesAsync()).FirstOrDefault(m => m.Hash == m1.Hash);
        var finalContact = await alice._database.GetContactAsync(bobContact.PublicKey);

        Assert.NotNull(finalM1);
        Assert.Equal(MessageDeliveryStatus.Delivered, finalM1.DeliveryStatus);
        Assert.NotNull(finalContact);
        Assert.Equal("AtPerch", finalContact.PigeonState);
    }

    [Fact]
    public async Task SendMessage_WhilePigeonInFlight_QueuesMessagesAndReleasesAllInBatchOnAck()
    {
        var (alice, bob, bobContact, _) = await SetupPairedEnginesAsync();

        // 1. Alice envoie M1 -> Pigeon prend son vol
        var m1 = await alice.SendMessageAsync("Premier message (M1)", bobContact.PublicKey);
        
        var contactAfterM1 = await alice._database.GetContactAsync(bobContact.PublicKey);
        Assert.NotNull(contactAfterM1);
        Assert.Equal("InFlight", contactAfterM1.PigeonState);

        // 2. Pendant que le pigeon est en vol, Alice envoie M2 et M3
        var m2 = await alice.SendMessageAsync("Deuxième message en file (M2)", bobContact.PublicKey);
        var m3 = await alice.SendMessageAsync("Troisième message en file (M3)", bobContact.PublicKey);

        // Vérifier que M2 et M3 sont bien en statut 'Queued'
        var queuedBefore = await alice._database.GetQueuedMessagesForContactAsync(bobContact.PublicKey);
        Assert.Equal(2, queuedBefore.Count);
        Assert.All(queuedBefore, q => Assert.Equal(MessageDeliveryStatus.Queued, q.DeliveryStatus));

        // Vérifier que M2 et M3 ne sont PAS encore dans le DAG mesh d'Alice (ils attendent au pigeonnier)
        Assert.False(alice._store.Contains(m2.Hash));
        Assert.False(alice._store.Contains(m3.Hash));

        // 3. Laisser l'ACK de M1 arriver chez Alice.
        // Quand l'ACK de M1 arrive, le moteur doit débloquer TOUS les messages de la queue (M2 et M3) en un seul lot !
        for (int i = 0; i < 50; i++)
        {
            var currentQueued = await alice._database.GetQueuedMessagesForContactAsync(bobContact.PublicKey);
            if (currentQueued.Count == 0)
            {
                // La file a été vidée !
                break;
            }
            await Task.Delay(30);
        }

        // 4. Assert intermédiaire : La file est vide, et les messages M2 et M3 sont partis en vol (InFlight)
        var queuedAfter = await alice._database.GetQueuedMessagesForContactAsync(bobContact.PublicKey);
        Assert.Empty(queuedAfter);

        var inFlightAfter = await alice._database.GetInFlightMessagesForContactAsync(bobContact.PublicKey);
        Assert.True(inFlightAfter.Count >= 2, $"Attendu >= 2 messages en vol, obtenu: {inFlightAfter.Count}");

        var contactDuringBatch = await alice._database.GetContactAsync(bobContact.PublicKey);
        Assert.NotNull(contactDuringBatch);
        Assert.Equal("InFlight", contactDuringBatch.PigeonState);
        Assert.Equal(2, contactDuringBatch.MessagesInFlightCount);

        // 5. Attendre la réception des ACKs pour le lot M2/M3
        for (int i = 0; i < 50; i++)
        {
            var contactFinal = await alice._database.GetContactAsync(bobContact.PublicKey);
            var remainingInFlight = await alice._database.GetInFlightMessagesForContactAsync(bobContact.PublicKey);
            if (remainingInFlight.Count == 0 && contactFinal?.PigeonState == "AtPerch")
            {
                break;
            }
            await Task.Delay(30);
        }

        // 6. Assert final : Tout est Delivered et le pigeon est au repos au perchoir
        var contactEnd = await alice._database.GetContactAsync(bobContact.PublicKey);
        Assert.NotNull(contactEnd);
        Assert.Equal("AtPerch", contactEnd.PigeonState);
        Assert.Equal(0, contactEnd.MessagesInFlightCount);

        var allAliceMessages = await alice._database.GetAllStoredMessagesAsync();
        var personalDelivered = allAliceMessages.Where(m => m.From == alice._myaccount!.Id && !m.IsAck).ToList();
        Assert.Equal(3, personalDelivered.Count);
        Assert.All(personalDelivered, m => Assert.Equal(MessageDeliveryStatus.Delivered, m.DeliveryStatus));
    }
}
