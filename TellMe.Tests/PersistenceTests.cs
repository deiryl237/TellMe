using TellMe.Core.Models;
using TellMe.Core.Storage;

namespace TellMe.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _dbPath;

    public PersistenceTests()
    {
        // Chaque test dispose de son propre fichier SQLite temporaire isolé
        _dbPath = Path.Combine(Path.GetTempPath(), $"tellme_test_{Guid.NewGuid()}.db3");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* Ignore si verrouillé brièvement */ }
        }
    }

    [Fact]
    public async Task SaveAccount_And_GetAccount_ReturnsPersistedProfile()
    {
        // 1. Arrange
        var db = new TellMeDatabase(_dbPath);
        await db.InitializeAsync();

        var original = new Account("Alice")
        {
            PigeonName = "Plume Blanche",
            FeatherColor = "Blanc",
            Accessory = "Écharpe Rouge",
            ExperiencePoints = 120
        };

        // 2. Act
        await db.SaveAccountAsync(original);
        var loaded = await db.GetAccountAsync();

        // 3. Assert
        Assert.NotNull(loaded);
        Assert.Equal("Alice", loaded.DisplayName);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Plume Blanche", loaded.PigeonName);
        Assert.Equal("Blanc", loaded.FeatherColor);
        Assert.Equal("Écharpe Rouge", loaded.Accessory);
        Assert.Equal(120, loaded.ExperiencePoints);
    }

    [Fact]
    public async Task SaveMessage_PersistsMessageAndRetrievesItAsMeshMessage()
    {
        // 1. Arrange
        var db = new TellMeDatabase(_dbPath);
        await db.InitializeAsync();

        var msg = new MeshMessage("Coucou du mesh", "Alice", "Bob", new[] { "hash_parent_1" });

        // 2. Act
        await db.SaveMessageAsync(msg, isPersonal: true, isOrphan: false);
        var all = await db.GetAllMessagesAsync();

        // 3. Assert
        Assert.Single(all);
        var retrieved = all[0];
        Assert.Equal(msg.Hash, retrieved.Hash);
        Assert.Equal("Coucou du mesh", retrieved.Message);
        Assert.Equal("Alice", retrieved.From);
        Assert.Equal("Bob", retrieved.To);
        Assert.Equal(new[] { "hash_parent_1" }, retrieved.Parents);
    }

    [Fact]
    public async Task EvictTransitMessages_PurgesOldTransit_PreservesPersonalAndRecentTransit()
    {
        // 1. Arrange
        var db = new TellMeDatabase(_dbPath);
        await db.InitializeAsync();

        // M1 : Message en transit vieux de 10 jours (doit être purgé !)
        var m1 = new StoredMessage
        {
            Hash = "hash_old_transit",
            Message = "Relais ancien",
            From = "Inconnu1",
            To = "Inconnu2",
            IsPersonal = false,
            ReceivedAt = DateTime.UtcNow.AddDays(-10)
        };

        // M2 : Message en transit récent de 2 jours (doit être conservé !)
        var m2 = new StoredMessage
        {
            Hash = "hash_recent_transit",
            Message = "Relais frais",
            From = "Inconnu3",
            To = "Inconnu4",
            IsPersonal = false,
            ReceivedAt = DateTime.UtcNow.AddDays(-2)
        };

        // M3 : Mon message personnel vieux de 15 jours (doit être conservé pour toujours !)
        var m3 = new StoredMessage
        {
            Hash = "hash_old_personal",
            Message = "Mon message privé",
            From = "Moi",
            To = "MonAmi",
            IsPersonal = true,
            ReceivedAt = DateTime.UtcNow.AddDays(-15)
        };

        await db.InsertStoredMessageAsync(m1);
        await db.InsertStoredMessageAsync(m2);
        await db.InsertStoredMessageAsync(m3);

        // 2. Act : On purge les messages de transit de plus de 7 jours
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await db.EvictTransitMessagesAsync(cutoff);

        // 3. Assert
        var remaining = await db.GetAllStoredMessagesAsync();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, m => m.Hash == "hash_old_transit");
        Assert.Contains(remaining, m => m.Hash == "hash_recent_transit");
        Assert.Contains(remaining, m => m.Hash == "hash_old_personal");
    }
}
