namespace TellMe.Core.Models;

public enum MessageDeliveryStatus
{
    Queued = 0,     // En attente au pigeonnier (le pigeon est déjà en vol)
    InFlight = 1,   // En vol avec le pigeon
    Delivered = 2,  // Livré (accusé de réception ACK reçu du destinataire)
    Received = 3    // Message entrant reçu d'un contact
}
