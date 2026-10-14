namespace TellMe.Core.Storage;

public static class TellMeDbPath{

    public static string dbpath(){
        string pt = Path.Combine(Path.GetTempPath(), $"tellme_test_{Guid.NewGuid()}.db3");
        return pt;
    }
}