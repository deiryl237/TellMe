using TellMe.Core.Engine;
using TellMe.Core.Models;
using System.Text.Json;

namespace TellMe.Core.Network;
public class SyncEngine{

    private readonly MessageStore _store;
    private readonly ITransport _transport;

    public SyncEngine(MessageStore store, ITransport transport){

        _transport = transport;
        _store = store;
        _transport.DataReceived += OnDataReceived;
        _transport.PeerConnected += async () =>
        {
            await StartSyncAsync();
        };


    }

    private async Task SendPacketAsync(SyncPacket packet){

        byte[] data = JsonSerializer.SerializeToUtf8Bytes(packet);
        await _transport.SendAsync(data);

    }

    public async Task StartSyncAsync(){

        var data = new SyncPacket();
        data.Type = "Heads";
        data.Heads = _store.Heads.ToArray();
        await SendPacketAsync(data);

    }

    private static List<SyncPacket> ParsePackets(byte[] data)
    {
        var packets = new List<SyncPacket>();
        ReadOnlySpan<byte> span = data;

        while (span.Length > 0)
        {
            while (span.Length > 0 && (span[0] == (byte)' ' || span[0] == (byte)'\n' || span[0] == (byte)'\r' || span[0] == (byte)'\t'))
            {
                span = span.Slice(1);
            }

            if (span.Length == 0 || span[0] != (byte)'{')
                break;

            var reader = new Utf8JsonReader(span);
            if (!reader.Read()) break;

            var packet = JsonSerializer.Deserialize<SyncPacket>(ref reader);
            if (packet != null)
            {
                packets.Add(packet);
            }

            if (reader.BytesConsumed <= 0) break;
            span = span.Slice((int)reader.BytesConsumed);
        }

        return packets;
    }

    private async void OnDataReceived(byte[] data)
    {
        try {
        var packets = ParsePackets(data);
            foreach(var packet in packets){
                await ProcessPacketAsync(packet);
                }
            }
        catch{}
    }
    
     private async Task ProcessPacketAsync(SyncPacket packet)
    {
        switch (packet.Type)
        {
            case "Heads":
                if (packet.Heads != null)
                {
                    foreach (string head in packet.Heads)
                    {
                        if (!_store.Contains(head))
                        {
                            var datarequest = new SyncPacket();
                            datarequest.Type = "Request";
                            datarequest.RequestedHash = head;
                            await SendPacketAsync(datarequest);
                        }
                    }
                }
                break;
            case "Request":
                if (packet.RequestedHash != null && _store.Contains(packet.RequestedHash))
                {
                    MeshMessage? message = _store.GetMessage(packet.RequestedHash);
                    if (message != null)
                    {
                        var messagepacket = new SyncPacket();
                        messagepacket.Type = "Message";
                        messagepacket.Message = message;
                        await SendPacketAsync(messagepacket);
                    }
                }
                break;
            case "Message":
                if (packet.Message != null)
                {
                    _store.AddMessage(packet.Message);
                    foreach (string parent in _store.MissingParents)
                    {
                        var request = new SyncPacket();
                        request.Type = "Request";
                        request.RequestedHash = parent;
                        await SendPacketAsync(request);
                    }
                }
                break;
        }
    }}


