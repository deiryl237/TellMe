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
    event Action? PeerConnected;
    // 5. Réinitialiser le cooldown de synchronisation pour forcer la reconnexion immédiate
    void ResetCooldown() { }
    // 6. Indique si un pair est actuellement connecté
    bool IsConnected => true;
    // 7. Notifie le transport que la synchronisation applicative est terminée (pour libérer le canal)
    void OnSyncCompleted() { }
    // 8. Cible prioritaire pour demande d'ami directe (force la connexion immédiate avec retry rapide)
    Task PrioritizeTargetAsync(string targetAddress, string? targetAccountId = null) => Task.CompletedTask;
    // 9. Mode récepteur prioritaire (pendant affichage QR code : écoute exclusive sans initier de connexion)
    void SetListeningPriority(bool isPriority) { }
    // 10. Adresse MAC du pair actuellement connecté
    string? ConnectedPeerAddress => null;
    // 11. Empreinte du compte local pour le phare BLE
    void SetMyAccountHash(byte[]? hash) { }
}