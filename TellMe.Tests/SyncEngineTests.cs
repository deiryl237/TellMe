using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Network;
using TellMe.Core.Storage;

namespace TellMe.Tests;

/// <summary>
/// Transport virtuel en mémoire simulant deux téléphones reliés par un canal direct.
/// </summary>
public class VirtualTransport : ITransport
{
    public static string _dbPath = TellMeDbPath.dbpath();
    public TellMeDatabase db = new TellMeDatabase(_dbPath);
    public Account aliceAccount = new Account("Alice");
    public Account malloryAccount = new Account("Mallory");
    public Account bobAccount = new Account("Bob");

    public VirtualTransport(){
        db.InitializeAsync();
        db.SaveAccountAsync(aliceAccount);
        db.SaveAccountAsync(malloryAccount);
        db.SaveAccountAsync(bobAccount);
    }
    public VirtualTransport? Partner { get; set; }
    public event Action<byte[]>? DataReceived;

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public async Task SendAsync(byte[] data)
    {
        if (Partner != null)
        {
            // Simule la transmission asynchrone dans l'air
            await Task.Yield();
            Partner.DataReceived?.Invoke(data);
        }
    }

    /// <summary>
    /// Crée une paire de transports virtuels reliés dos-à-dos.
    /// </summary>
    public static (VirtualTransport Alice, VirtualTransport Bob) CreatePair()
    {
        var alice = new VirtualTransport();
        var bob = new VirtualTransport();
        alice.Partner = bob;
        bob.Partner = alice;
        return (alice, bob);
    }
}

public class SyncEngineTests
{
    public static string _dbPath = TellMeDbPath.dbpath();
    public TellMeDatabase db = new TellMeDatabase(_dbPath);
    public Account aliceAccount = new Account("Alice");
    public Account malloryAccount = new Account("Mallory");
    public Account bobAccount = new Account("Bob");

    public SyncEngineTests(){
        db.InitializeAsync();
        db.SaveAccountAsync(aliceAccount);
        db.SaveAccountAsync(malloryAccount);
        db.SaveAccountAsync(bobAccount);
    }
    [Fact]
    public async Task StartSync_TwoNodesWithDifferentMessages_ConvergeAcrossNetwork()
    {
        
        // 1. Arrange : Alice et Bob ont chacun un store et un message distinct
        var aliceStore = new MessageStore();
        var bobStore = new MessageStore();

        var m1 = MeshMessage.CreateSigned("Message d'Alice", "Tous", Array.Empty<string>(), aliceAccount.Id, aliceAccount.PrivateKey);
        var m2 = MeshMessage.CreateSigned("Message de Bob", "Tous", Array.Empty<string>(), bobAccount.Id, bobAccount.PrivateKey);

        aliceStore.AddMessage(m1);
        bobStore.AddMessage(m2);

        // Connexion réseau virtuelle entre les deux
        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var aliceEngine = new SyncEngine(aliceStore, aliceTransport);
        var bobEngine = new SyncEngine(bobStore, bobTransport);

        // 2. Act : Déclenchement de la synchronisation Gossip bidirectionnelle
        await aliceEngine.StartSyncAsync();
        await bobEngine.StartSyncAsync();

        // Attendre que la synchronisation Gossip bidirectionnelle converge
        for (int i = 0; i < 40 && (aliceStore.Count < 2 || bobStore.Count < 2); i++)
        {
            await Task.Delay(50);
        }

        // 3. Assert : Les deux téléphones ont convergé et possèdent les deux messages !
        Assert.Equal(2, aliceStore.Count);
        Assert.Equal(2, bobStore.Count);

        Assert.True(aliceStore.Contains(m1.Hash) && aliceStore.Contains(m2.Hash));
        Assert.True(bobStore.Contains(m1.Hash) && bobStore.Contains(m2.Hash));

        Assert.Equal(2, aliceStore.Heads.Count);
        Assert.Equal(2, bobStore.Heads.Count);
    }

    [Fact]
    public async Task StartSync_LinearChainOfMessages_ResolvesOrphansInCascadeAcrossNetwork()
    {
        // 1. Arrange : Alice a une chaîne linéaire de 3 messages causaux (M1 -> M2 -> M3)
        var aliceStore = new MessageStore();
        var bobStore = new MessageStore();

        var m1 = MeshMessage.CreateSigned("Premier", "Tous", Array.Empty<string>(), aliceAccount.Id, aliceAccount.PrivateKey);
        var m2 = MeshMessage.CreateSigned("Deuxième", "Tous", new[] { m1.Hash }, aliceAccount.Id, aliceAccount.PrivateKey);
        var m3 = MeshMessage.CreateSigned("Troisième", "Tous", new[] { m2.Hash }, aliceAccount.Id, aliceAccount.PrivateKey);

        aliceStore.AddMessage(m1);
        aliceStore.AddMessage(m2);
        aliceStore.AddMessage(m3);

        // Alice n'a qu'une seule tête : M3
        Assert.Single(aliceStore.Heads);
        Assert.Contains(m3.Hash, aliceStore.Heads);

        // Bob n'a encore aucun message
        Assert.Equal(0, bobStore.Count);

        var (aliceTransport, bobTransport) = VirtualTransport.CreatePair();
        var aliceEngine = new SyncEngine(aliceStore, aliceTransport);
        var bobEngine = new SyncEngine(bobStore, bobTransport);

        // 2. Act : Alice annonce ses têtes à Bob (M3), Bob tire les ancêtres en cascade
        await aliceEngine.StartSyncAsync();

        // Attendre que la résolution en cascade des orphelins se termine
        for (int i = 0; i < 40 && bobStore.Count < 3; i++)
        {
            await Task.Delay(50);
        }

        // 3. Assert : Bob a résolu tous ses orphelins et possède les 3 messages de la chaîne !
        Assert.Equal(3, bobStore.Count);
        Assert.Equal(0, bobStore.BufferCount);
        Assert.Single(bobStore.Heads);
        Assert.Contains(m3.Hash, bobStore.Heads);
    }
}
