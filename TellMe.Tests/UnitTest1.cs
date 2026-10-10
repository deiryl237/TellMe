using TellMe.Core.Engine;
using TellMe.Core.Models;

namespace TellMe.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        string[] set1 = new[] {"hash1","hash2"};
        string[] set2 = new[] {"hash2","hash1"};
        string[] set3 = new[] {"hash2","hash3"};
        var msg1 = new MeshMessage("Bonjour","Daniel","sophie",set1);
        var msg2 = new MeshMessage("Bonjour","Daniel","sophie",set2);
        var msg3 = new MeshMessage("Bonjour","Daniel","sophie",set3);
        Assert.Equal(msg1.Hash,msg2.Hash);
        Assert.NotEqual(msg1.Hash,msg3.Hash);
    }

    [Fact]
    public void AddMessage_GenesisMessage_IsAddedToCollectionAndBecomesHead()
    {
        // 1. Arrange
        var store = new MessageStore();
        var msgGenesis = new MeshMessage("Premier message", "Alice", "Bob", parents: Array.Empty<string>());

        // 2. Act
        store.AddMessage(msgGenesis);

        // 3. Assert
        Assert.True(store.Contains(msgGenesis.Hash));
        Assert.Contains(msgGenesis.Hash, store.Heads);
        Assert.Single(store.Heads);
        Assert.Equal(0, store.BufferCount);
    }

    [Fact]
    public void AddMessage_DuplicateMessage_IsIgnoredWithoutError()
    {
        // 1. Arrange
        var store = new MessageStore();
        var msg = new MeshMessage("Bonjour", "Alice", "Bob", parents: Array.Empty<string>());

        // 2. Act : On ajoute le message une première fois, puis une deuxième fois
        store.AddMessage(msg);
        store.AddMessage(msg);

        // 3. Assert : Il ne doit y avoir qu'un seul message, aucune tête dupliquée
        Assert.Equal(1, store.Count);
        Assert.Single(store.Heads);
    }

    [Fact]
    public void AddMessage_LinearChildMessage_ReplacesParentInHeads()
    {
        // 1. Arrange
        var store = new MessageStore();
        var msg1 = new MeshMessage("Premier message", "Alice", "Bob", parents: Array.Empty<string>());
        var msg2 = new MeshMessage("Deuxième message", "Bob", "Alice", parents: new[] { msg1.Hash });

        // 2. Act
        store.AddMessage(msg1);
        store.AddMessage(msg2);

        // 3. Assert
        Assert.Equal(2, store.Count);
        Assert.DoesNotContain(msg1.Hash, store.Heads); // M1 n'est plus une tête !
        Assert.Contains(msg2.Hash, store.Heads);        // M2 est la nouvelle tête unique
        Assert.Single(store.Heads);
    }

    [Fact]
    public void AddMessage_OrphanMessage_GoesToBufferAndIsResolvedWhenParentArrives()
    {
        // 1. Arrange
        var store = new MessageStore();
        var msg1 = new MeshMessage("Premier message", "Alice", "Bob", parents: Array.Empty<string>());
        var msg2 = new MeshMessage("Deuxième message (orphelin)", "Bob", "Alice", parents: new[] { msg1.Hash });

        // 2. Act 1 : L'enfant M2 arrive EN PREMIER !
        store.AddMessage(msg2);

        // 3. Assert 1 : M2 ne doit PAS être dans la collection officielle, il doit être dans le Sas
        Assert.False(store.Contains(msg2.Hash));
        Assert.Equal(0, store.Count);
        Assert.Equal(1, store.BufferCount);
        Assert.Empty(store.Heads);

        // 4. Act 2 : Le parent M1 arrive enfin !
        store.AddMessage(msg1);

        // 5. Assert 2 : M1 et M2 sont maintenant tous les deux validés, le Sas est vidé
        Assert.True(store.Contains(msg1.Hash));
        Assert.True(store.Contains(msg2.Hash));
        Assert.Equal(2, store.Count);
        Assert.Equal(0, store.BufferCount);
        Assert.Contains(msg2.Hash, store.Heads);
        Assert.DoesNotContain(msg1.Hash, store.Heads);
        Assert.Single(store.Heads);
    }
}




