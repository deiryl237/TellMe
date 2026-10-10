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
        if (MessageCollection.ContainsKey(message.Hash))
        {return;}
        List<string> missingParents = new List<string>();
        foreach (string parent in message.Parents){
            if (!MessageCollection.ContainsKey(parent)){
                missingParents.Add(parent);
            }
        }
        if (missingParents.Count>0){
           foreach(string missed in missingParents) //Le message est orphelin, on le range dans le buffer, le temps de trouver son parent
            {
                if(!Buffer.ContainsKey(missed)){
                    Buffer[missed] = new List<MeshMessage>();
                }
                Buffer[missed].Add(message);
            }
            return;
        }
        if (missingParents.Count == 0){
            MessageCollection.Add(message.Hash,message);
            foreach(string oldhead in message.Parents)
            {
                CurrentsHeads.Remove(oldhead);
            }
            CurrentsHeads.Add(message.Hash);
        }
        if(Buffer.ContainsKey(message.Hash)){
            List<MeshMessage> Orphans = Buffer[message.Hash]; 
            Buffer.Remove(message.Hash);//on vide le buffer

            foreach(MeshMessage orphan in Orphans ){
                AddMessage(orphan);
            }

        }
    }
}