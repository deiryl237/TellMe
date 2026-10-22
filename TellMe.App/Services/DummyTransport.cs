using TellMe.Core.Network;

namespace TellMe.App.Services;

/// <summary>
/// Transport factice utilisé sur les plateformes sans Bluetooth RFCOMM natif (ex: simulateur Windows/Mac).
/// </summary>
public class DummyTransport : ITransport
{
    public event Action<byte[]>? DataReceived;
    public event Action? PeerConnected;

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task SendAsync(byte[] data) => Task.CompletedTask;
    public void ResetCooldown() { }
}
