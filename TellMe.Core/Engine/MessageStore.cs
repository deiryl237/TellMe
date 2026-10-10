using TellMe.Core.Models;

namespace TellMe.Core.Engine;

public class MessageStore{

    private readonly Dictionary<string, MeshMessage> MessageCollection = new();
    private readonly HashSet<string> CurrentsHeads = new();
    private readonly Dictionary<string, List<MeshMessage>> Buffer = new();

    public int Count => MessageCollection.Count;
    public bool Contains(string hash) => MessageCollection.ContainsKey(hash);
    public IReadOnlyCollection<string> Heads => CurrentsHeads;
    public int BufferCount => Buffer.Count;

    public MessageStore()
    {
    }

    public void AddMessage(MeshMessage message)
    {
        
    }
}