using Tellme.Core.Models;

namespace TellMe.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        string[] set1 = new[] {"hash1","hash2"};
        string[] set2 = new[] {"hash2","hash1"};
        var msg1 = new MeshMessage("Bonjour","Daniel","sophie",set1);
        var msg2 = new MeshMessage("Bonjour","Daniel","sophie",set2);
        Assert.Equal(msg1.Hash,msg2.Hash);
    }
}
