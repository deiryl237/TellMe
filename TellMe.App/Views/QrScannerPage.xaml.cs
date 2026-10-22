using System.Text.Json;
using TellMe.Core.Models;
using ZXing.Net.Maui;

namespace TellMe.App.Views;

public partial class QrScannerPage : ContentPage
{
    private readonly Action<ContactQrCard> _onCardScanned;
    private bool _hasScanned = false;

    public QrScannerPage(Action<ContactQrCard> onCardScanned)
    {
        InitializeComponent();
        _onCardScanned = onCardScanned;

        BarcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false
        };
    }

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        if (_hasScanned) return;

        var first = e.Results.FirstOrDefault();
        if (first == null || string.IsNullOrWhiteSpace(first.Value)) return;

        try
        {
            var card = JsonSerializer.Deserialize<ContactQrCard>(first.Value);
            if (card != null && !string.IsNullOrEmpty(card.id))
            {
                _hasScanned = true;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Navigation.PopModalAsync();
                    _onCardScanned.Invoke(card);
                });
            }
        }
        catch
        {
            // Ce n'était pas un QR code TellMe, on continue de chercher
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
