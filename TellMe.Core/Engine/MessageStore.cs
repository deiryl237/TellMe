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
    public IEnumerable<string> MissingParents => Buffer.Keys; //enumere les parents manquants dans une liste de string


    public MessageStore()
    {
    }
    public MeshMessage? GetMessage(string hash){
        return
        MessageCollection.GetValueOrDefault(hash); //recupere un message grace à sa clé dans le dictionnaire MessageCollection 
    }
    public void AddMessage(MeshMessage message)
    {
        if (MessageCollection.ContainsKey(message.Hash))
        {return;} //si la collection contient déjà le message on s'arrête là
        List<string> missingParents = new List<string>(); //sinon on va vérifier s'il est orphelin ou pas, on créé une liste missingparents
        foreach (string parent in message.Parents){ 
            if (!MessageCollection.ContainsKey(parent)){//on vérifie si chaque parent du message se trouve déjà dans la collection
                missingParents.Add(parent);//Pour chaque parent qu'on ne trouve pas, on l'ajoute aux missing parents
            }
        }
        if (missingParents.Count>0){ //Si le message a des parents manquant
           foreach(string missed in missingParents) //Le message est orphelin, on le range dans le buffer, le temps de trouver son parent
            {
                if(!Buffer.ContainsKey(missed)){
                    Buffer[missed] = new List<MeshMessage>();
                }
                Buffer[missed].Add(message);
            }
            return;
        }
        if (missingParents.Count == 0){ //si le message n'a pas de parents manquants
            MessageCollection.Add(message.Hash,message);//on l'ajoute à  la collection
            foreach(string oldhead in message.Parents)
            {
                CurrentsHeads.Remove(oldhead); //On enlève ses parents des têtes
            }
            CurrentsHeads.Add(message.Hash); //On l'ajoute lui comme nouvelle tête
        }
        if(Buffer.ContainsKey(message.Hash)){ //On vérifie s'il ya des messages dans le buffer qui l'attendait
            List<MeshMessage> Orphans = Buffer[message.Hash]; 
            Buffer.Remove(message.Hash);//on vide le buffer

            foreach(MeshMessage orphan in Orphans ){
                AddMessage(orphan); //on envoi les messages qui l'attendaient à la méthode AddMessage
            }

        }
    }
   
}