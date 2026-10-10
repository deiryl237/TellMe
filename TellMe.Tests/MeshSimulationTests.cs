using TellMe.Core.Engine;
using TellMe.Core.Models;

namespace TellMe.Tests;

public class MeshSimulationTests
{
    /// <summary>
    /// Synchronise deux téléphones de manière bidirectionnelle lors d'une rencontre Bluetooth.
    /// </summary>
    private static void Sync(MessageStore a, MessageStore b)
    {
        SyncOneWay(a, b); // A transmet à B ce qui lui manque
        SyncOneWay(b, a); // B transmet à A ce qui lui manque
    }

    /// <summary>
    /// Transmet les messages manquants de la source vers la cible.
    /// </summary>
    private static void SyncOneWay(MessageStore source, MessageStore target)
    {
        foreach (string head in source.Heads){
            if (target.Contains(head)){
                continue;
            }
            var msg = source.GetMessage(head);
            if (msg != null)
            target.AddMessage(msg);
        }
        while (target.BufferCount > 0){
            bool hasProgressed = false;
            foreach(string missedparent in target.MissingParents.ToList() ){
                if (source.Contains(missedparent)){
                    var msg = source.GetMessage(missedparent);
                    if (msg != null){
                    target.AddMessage(msg);
                    hasProgressed = true;}
                }
                continue;
            }
            if (!hasProgressed)
            break;
        }
        // TODO: À implémenter ensemble pour l'Étape 2 !
    }

    [Fact]
    public void ThreeNodes_PassingMessagesOpportunistically_ConvergeToSameGraph()
    {
        // 1. Arrange : Trois téléphones isolés dans la nature
        var alice = new MessageStore();
        var bob = new MessageStore();
        var charlie = new MessageStore();

        // Alice écrit M1 (le matin de son côté)
        var m1 = new MeshMessage("Bonjour d'Alice", "Alice", "Tous", Array.Empty<string>());
        alice.AddMessage(m1);

        // Bob écrit M2 (le matin de son côté, sans connaître M1)
        var m2 = new MeshMessage("Bonjour de Bob", "Bob", "Tous", Array.Empty<string>());
        bob.AddMessage(m2);

        // 2. Act : Les rencontres de la journée !
        // À midi : Alice croise Charlie au café (Rencontre A <-> C)
        Sync(alice, charlie);

        // À 17h : Charlie croise Bob dans le métro (Rencontre C <-> B)
        // (Bob reçoit M1 via Charlie, Charlie reçoit M2 de Bob)
        Sync(charlie, bob);

        // À 20h : Alice croise Charlie en rentrant
        Sync(alice, charlie);

        // 3. Assert : Convergence totale du réseau Mesh !
        // Tous les trois doivent avoir les 2 messages et les 2 têtes [M1, M2] !
        Assert.Equal(2, alice.Count);
        Assert.Equal(2, bob.Count);
        Assert.Equal(2, charlie.Count);

        Assert.True(alice.Contains(m1.Hash) && alice.Contains(m2.Hash));
        Assert.True(bob.Contains(m1.Hash) && bob.Contains(m2.Hash));
        Assert.True(charlie.Contains(m1.Hash) && charlie.Contains(m2.Hash));

        Assert.Equal(2, alice.Heads.Count);
        Assert.Equal(2, bob.Heads.Count);
        Assert.Equal(2, charlie.Heads.Count);
    }
}
