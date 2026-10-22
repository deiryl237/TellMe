using System.Text.Json;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Network;
using Xunit.Abstractions;

namespace TellMe.Tests;

/// <summary>
/// Transport qui imite un vrai socket RFCOMM Android : le flux d'octets est découpé
/// en trames de taille MTU (990 octets, valeur relevée dans le logcat du Xiaomi :
/// "RFCOMM_CreateConnection ... mtu=990"). Chaque trame arrive comme un DataReceived séparé.
/// </summary>
public class ChunkingTransport : ITransport
{
    private readonly int _mtu;
    public ChunkingTransport(int mtu) => _mtu = mtu;

    public ChunkingTransport? Partner { get; set; }
    public event Action<byte[]>? DataReceived;
    public event Action? PeerConnected;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public async Task SendAsync(byte[] data)
    {
        if (Partner == null) return;
        await Task.Yield();
        for (int offset = 0; offset < data.Length; offset += _mtu)
        {
            int len = Math.Min(_mtu, data.Length - offset);
            Partner.DataReceived?.Invoke(data.AsSpan(offset, len).ToArray());
        }
    }

    public void RaisePeerConnected() => PeerConnected?.Invoke();
}

/// <summary>
/// Transport fidèle à un vrai socket : tous les envois s'ajoutent à UN flux continu,
/// et chaque lecture rend jusqu'à MTU octets de ce qui est disponible, sans respecter
/// les frontières des paquets (exactement comme InputStream.Read sur Android).
/// </summary>
public class StreamTransport : ITransport
{
    private readonly int _mtu;
    private readonly List<byte> _pending = new();
    private readonly object _lock = new();
    private bool _pumping;

    public StreamTransport(int mtu) => _mtu = mtu;
    public StreamTransport? Partner { get; set; }
    public event Action<byte[]>? DataReceived;
    public event Action? PeerConnected;
    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public Task SendAsync(byte[] data)
    {
        Partner?.Enqueue(data);
        return Task.CompletedTask;
    }

    private void Enqueue(byte[] data)
    {
        lock (_lock)
        {
            _pending.AddRange(data);
            if (_pumping) return;
            _pumping = true;
        }
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(5); // le temps que d'autres écritures s'accumulent, comme dans le buffer noyau
                byte[] chunk;
                lock (_lock)
                {
                    if (_pending.Count == 0) { _pumping = false; return; }
                    int n = Math.Min(_mtu, _pending.Count);
                    chunk = _pending.GetRange(0, n).ToArray();
                    _pending.RemoveRange(0, n);
                }
                DataReceived?.Invoke(chunk);
            }
        });
    }

    public void RaisePeerConnected() => PeerConnected?.Invoke();
}

/// <summary>
/// Tests d'audit : ils reproduisent les conditions réelles du Bluetooth.
/// </summary>
public class RealisticTransportTests
{
    private readonly ITestOutputHelper _out;
    public RealisticTransportTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Audit_MeasureRealPacketSizes()
    {
        var alice = new Account("Alice");
        var m = MeshMessage.CreateSigned("Salut", "PUBLICBOARD:Campus", Array.Empty<string>(), alice.Id, alice.PrivateKey);
        var withParents = MeshMessage.CreateSigned("Salut", "PUBLICBOARD:Campus",
            new[] { m.Hash, new string('A', 64), new string('B', 64) }, alice.Id, alice.PrivateKey);

        int Size(SyncPacket p) => JsonSerializer.SerializeToUtf8Bytes(p).Length;

        _out.WriteLine($"Message, 0 parent  : {Size(new SyncPacket { Type = "Message", Message = m })} octets");
        _out.WriteLine($"Message, 3 parents : {Size(new SyncPacket { Type = "Message", Message = withParents })} octets");
        _out.WriteLine($"ContactRequest     : {Size(new SyncPacket { Type = "ContactRequest", request = new ContactRequest(alice, new Account("Bob").Id) })} octets");
        _out.WriteLine("MTU RFCOMM relevé  : 990 octets");
    }

    [Fact]
    public async Task Audit_LargeMessage_OverRealRfcommMtu_IsDelivered()
    {
        // Arrange : Alice possède 4 messages indépendants puis un 5e qui les cite tous comme parents
        var alice = new Account("Alice");
        var aliceStore = new MessageStore();
        var bobStore = new MessageStore();

        for (int i = 0; i < 4; i++)
        {
            aliceStore.AddMessage(MeshMessage.CreateSigned($"msg {i}", "PUBLICBOARD:Campus",
                Array.Empty<string>(), alice.Id, alice.PrivateKey));
        }
        aliceStore.AddMessage(MeshMessage.CreateSigned("Le message qui cite tout le monde",
            "PUBLICBOARD:Campus", aliceStore.Heads.ToArray(), alice.Id, alice.PrivateKey));
        Assert.Equal(5, aliceStore.Count);

        var aliceT = new ChunkingTransport(990);
        var bobT = new ChunkingTransport(990);
        aliceT.Partner = bobT;
        bobT.Partner = aliceT;

        var aliceSync = new SyncEngine(aliceStore, aliceT);
        var bobSync = new SyncEngine(bobStore, bobT);

        // Act : Alice annonce ses têtes à Bob
        await aliceSync.StartSyncAsync();
        await Task.Delay(1500);

        // Assert : Bob doit avoir récupéré les 5 messages
        _out.WriteLine($"Bob a reçu {bobStore.Count}/5 messages (buffer orphelins : {bobStore.BufferCount})");
        Assert.Equal(5, bobStore.Count);
    }

    [Theory]
    [InlineData(1_000_000)] // témoin : tuyau qui ne coupe jamais les paquets
    [InlineData(990)]       // réalité : MTU RFCOMM relevé dans le logcat
    public async Task Audit_Backlog_OverContinuousSocketStream_IsDelivered(int mtu)
    {
        // Arrange : Alice a posté 10 messages indépendants sur le Campus pendant que Bob était absent
        var alice = new Account("Alice");
        var aliceStore = new MessageStore();
        var bobStore = new MessageStore();
        for (int i = 0; i < 10; i++)
        {
            aliceStore.AddMessage(MeshMessage.CreateSigned($"campus {i}", "PUBLICBOARD:Campus",
                Array.Empty<string>(), alice.Id, alice.PrivateKey));
        }

        var aliceT = new StreamTransport(mtu);
        var bobT = new StreamTransport(mtu);
        aliceT.Partner = bobT;
        bobT.Partner = aliceT;
        var aliceSync = new SyncEngine(aliceStore, aliceT);
        var bobSync = new SyncEngine(bobStore, bobT);

        // Act : comme sur le vrai téléphone, les DEUX côtés envoient leurs têtes à la connexion
        await aliceSync.StartSyncAsync();
        await bobSync.StartSyncAsync();
        await Task.Delay(2000);

        // Assert
        _out.WriteLine($"MTU {mtu} : Bob a reçu {bobStore.Count}/10 messages");
        Assert.Equal(10, bobStore.Count);
    }
}
