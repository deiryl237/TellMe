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

    private async void OnDataReceived(byte[] data){

        var packet = JsonSerializer.Deserialize<SyncPacket>(data);
        if (packet == null) return;

        switch (packet.Type){

            case "Heads" :
                foreach (string head in packet.Heads){
                    if(!_store.Contains(head)){
                        var datarequest = new SyncPacket();
                        datarequest.Type = "Request";
                        datarequest.RequestedHash = head;
                        await SendPacketAsync(datarequest);
                    }
                }
            break;
            case "Request":

                if (packet.RequestedHash != null && _store.Contains(packet.RequestedHash)){
                MeshMessage message = _store.GetMessage(packet.RequestedHash);
                var messagepacket = new SyncPacket();
                messagepacket.Type = "Message";
                messagepacket.Message = message;
                await SendPacketAsync(messagepacket);
                }


            break;
            case "Message":
                if (packet.Message != null){
                    _store.AddMessage(packet.Message);
                    foreach(string parent in _store.MissingParents){
                        var request = new SyncPacket();
                        request.Type = "Request";
                        request.RequestedHash = parent;
                        await SendPacketAsync(request);
                    }
                }

            break;
        }

    }

}

