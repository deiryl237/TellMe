using TellMe.App.ViewModels;
using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.Views;

#if ANDROID
using TellMe.App.Platforms.Android;
#endif

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public MainPage(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        BindingContext = _viewModel;

        // Dialogue automatique : Accepter / Refuser une demande d'ami entrante
        _viewModel.ContactRequestPromptRequested += async request =>
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                Page targetPage = Navigation.ModalStack.LastOrDefault() ?? this;

                bool accepted = await targetPage.DisplayAlert(
                    "Nouvelle demande d'ami 🕊️",
                    $"{request.Name} ({request.Pigeon}) souhaite vous ajouter dans ses contacts.",
                    "Accepter",
                    "Refuser");

                if (accepted && Navigation.ModalStack.Count > 0)
                {
                    try { await Navigation.PopModalAsync(); } catch { }
                }

                return accepted;
            });
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        await AndroidBluetoothTransport.EnsurePermissionsAsync();
#endif
        await _viewModel.InitializeAsync();
    }

    private void OnBottomBarTabChanged(object? sender, int newIndex)
    {
        SwitchTab(newIndex);
    }

    private void SwitchTab(int index)
    {
        TabHome.IsVisible = index == 0;
        TabChats.IsVisible = index == 1;
        TabCampus.IsVisible = index == 2;
        TabWardrobe.IsVisible = index == 3;
    }

    private async void OnChangeNameClicked(object sender, EventArgs e)
    {
        string? newName = await DisplayPromptAsync("Mon Profil", "Entrez votre nouveau pseudo :", initialValue: _viewModel.DisplayName);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            await _viewModel.UpdateDisplayNameAsync(newName);
        }
    }

    private async void OnShowMyQrClicked(object sender, EventArgs e)
    {
        var card = _viewModel.GetMyQrCard();
        if (card == null)
        {
            await DisplayAlert("Erreur", "Profil non initialisé.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new QrDisplayPage(card, _viewModel.Transport));
    }

    private async void OnAddContactClicked(object sender, EventArgs e)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("Caméra requise", "L'accès à la caméra est nécessaire pour scanner le QR Code.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new QrScannerPage(async card =>
        {
            await _viewModel.AddContactFromQrAsync(card);
            await DisplayAlert("Demande transmise ", $"Demande envoyée à {card.DisplayName} !", "OK");
        }));
    }

    private async void OnConversationTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is Contact selectedContact)
        {
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            await Navigation.PushAsync(new ChatPage(chatVm, selectedContact));
        }
    }

    private async void OnOpenCampusBoardClicked(object sender, EventArgs e)
    {
        var campus = _viewModel.CampusContact;
        if (campus == null)
        {
            await DisplayAlert("Place Publique", "Initialisation du babillard...", "OK");
            await _viewModel.LoadConversationsAsync();
            campus = _viewModel.CampusContact;
        }

        if (campus != null)
        {
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            await Navigation.PushAsync(new ChatPage(chatVm, campus));
        }
    }

    private async void OnSavePigeonClicked(object sender, EventArgs e)
    {
        string newName = PigeonNameEntry.Text?.Trim() ?? _viewModel.PigeonName;
        string feather = (string?)FeatherPicker.SelectedItem ?? _viewModel.FeatherColor;
        string accessory = (string?)AccessoryPicker.SelectedItem ?? _viewModel.Accessory;

        await _viewModel.SavePigeonProfileAsync(newName, feather, accessory);
        await DisplayAlert("Garde-Robe 🕊️", "Le look de ton pigeon voyageur a été mis à jour !", "Super");
    }
}
