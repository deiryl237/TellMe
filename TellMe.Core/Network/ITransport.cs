namespace TellMe.Core.Network;

public interface ITransport{

    // 1. Démarrer le transport (allumer le Bluetooth / ouvrir le serveur)
    Task StartAsync();
    // 2. Arrêter le transport (couper les connexions propres)
    Task StopAsync();
    // 3. Envoyer des octets bruts au pair connecté
    Task SendAsync(byte[] data);
    // 4. L'événement déclenché quand des octets arrivent de l'autre téléphone
    event Action<byte[]>? DataReceived;

}