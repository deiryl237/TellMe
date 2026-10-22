using QRCoder;
using TellMe.Core.Models;
using TellMe.Core.Network;

namespace TellMe.App.Views;

public partial class QrDisplayPage : ContentPage
{
    private readonly ITransport? _transport;

    public QrDisplayPage(ContactQrCard card, ITransport? transport = null)
    {
        InitializeComponent();
        _transport = transport;

        NameLabel.Text = card.DisplayName;
        PigeonLabel.Text = $"Pigeon : {card.PigeonName}";

        // Génère les octets PNG du QR code en pur C# avec QRCoder
        string json = System.Text.Json.JsonSerializer.Serialize(card);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(data);
        byte[] qrBytes = qrCode.GetGraphic(20);

        QrImage.Source = ImageSource.FromStream(() => new MemoryStream(qrBytes));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _transport?.SetListeningPriority(true);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _transport?.SetListeningPriority(false);
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
