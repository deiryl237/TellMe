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
                    "Nouvelle demande d'ami",
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

        // Écouter les changements dans la liste des contacts pour mettre à jour la roue Rive
        _viewModel.PrivateChats.CollectionChanged += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(UpdateRadialArcSlots);
        };
        _viewModel.WheelContacts.CollectionChanged += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(UpdateRadialArcSlots);
        };
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPigeonContact) ||
                e.PropertyName == nameof(MainViewModel.ActivePigeonStatusBadge) ||
                e.PropertyName == nameof(MainViewModel.IsActivePigeonInFlight))
            {
                MainThread.BeginInvokeOnMainThread(UpdateRadialArcSlots);
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        await AndroidBluetoothTransport.EnsurePermissionsAsync();
#endif
        await _viewModel.InitializeAsync();
        UpdateRadialArcSlots();
    }

    private void UpdateRadialArcSlots()
    {
        if (_viewModel.WheelContacts == null) return;
        var wheelContacts = _viewModel.WheelContacts.ToList();

        int selectedIndex = 0;
        if (_viewModel.SelectedPigeonContact != null)
        {
            int idx = wheelContacts.FindIndex(c => c.PublicKey == _viewModel.SelectedPigeonContact.PublicKey);
            if (idx >= 0) selectedIndex = idx;
        }

        TabHome.SetWheelContacts(wheelContacts, selectedIndex);
        TabHome.UpdateStatusBadge(_viewModel.ActivePigeonStatusBadge, _viewModel.IsActivePigeonInFlight);
    }

    private void OnWheelSlotSelected(object? sender, int slotIndex)
    {
        var wheelContacts = _viewModel.WheelContacts.ToList();
        if (slotIndex >= 0 && slotIndex < wheelContacts.Count)
        {
            _viewModel.SelectedPigeonContact = wheelContacts[slotIndex];
        }
        else
        {
            _viewModel.SelectedPigeonContact = null;
        }

        TabHome.UpdateStatusBadge(_viewModel.ActivePigeonStatusBadge, _viewModel.IsActivePigeonInFlight);
    }

    private async void OnWheelAddSlotClicked(object? sender, EventArgs e)
    {
        string action = await DisplayActionSheet(
            "Roue des raccourcis",
            "Annuler",
            null,
            "Ajouter un contact existant sur la roue",
            "Scanner un nouveau contact (QR Code)");

        if (action == "Ajouter un contact existant sur la roue")
        {
            await PromptAddExistingContactToWheelAsync();
        }
        else if (action == "Scanner un nouveau contact (QR Code)")
        {
            OnAddContactClicked(this, EventArgs.Empty);
        }
    }

    private async Task PromptAddExistingContactToWheelAsync()
    {
        var allPrivate = _viewModel.PrivateChats.ToList();
        var pinnedKeys = _viewModel.WheelContacts.Select(c => c.PublicKey).ToHashSet();
        var available = allPrivate.Where(c => !pinnedKeys.Contains(c.PublicKey)).ToList();

        if (available.Count == 0)
        {
            if (allPrivate.Count == 0)
            {
                bool scan = await DisplayAlert(
                    "Aucun contact",
                    "Vous n'avez pas encore d'amis dans votre carnet. Voulez-vous en scanner un via QR Code ?",
                    "Scanner QR",
                    "Annuler");
                if (scan)
                {
                    OnAddContactClicked(this, EventArgs.Empty);
                }
            }
            else
            {
                await DisplayAlert(
                    "Roue complète",
                    "Tous vos contacts sont déjà épinglés sur votre roue de raccourcis !",
                    "Compris");
            }
            return;
        }

        if (_viewModel.WheelContacts.Count >= 8)
        {
            await DisplayAlert(
                "Roue au maximum",
                "La roue contient déjà 8 contacts. Retirez-en un pour pouvoir en ajouter un autre.",
                "OK");
            return;
        }

        string[] contactChoices = available
            .Select(c => $"{c.DisplayName} ({c.PigeonName}) - {c.ShortId}")
            .ToArray();

        string chosen = await DisplayActionSheet(
            "Choisir un contact à épingler sur la roue",
            "Annuler",
            null,
            contactChoices);

        if (string.IsNullOrEmpty(chosen) || chosen == "Annuler") return;

        int index = Array.IndexOf(contactChoices, chosen);
        if (index >= 0 && index < available.Count)
        {
            var contactToAdd = available[index];
            _viewModel.AddContactToWheel(contactToAdd);
            UpdateRadialArcSlots();
        }
    }

    private async void OnRemoveContactFromWheelClicked(object? sender, EventArgs e)
    {
        var contact = _viewModel.SelectedPigeonContact;
        if (contact == null) return;

        bool confirm = await DisplayAlert(
            "Retirer de la roue",
            $"Voulez-vous retirer {contact.DisplayName} de votre roue de raccourcis ?",
            "Retirer",
            "Annuler");

        if (confirm)
        {
            _viewModel.RemoveContactFromWheel(contact);
            UpdateRadialArcSlots();
        }
    }

    private async void OnWheelPebbleClicked(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedPigeonContact != null)
        {
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            await Navigation.PushAsync(new ChatPage(chatVm, _viewModel.SelectedPigeonContact));
        }
        else
        {
            await PromptAddExistingContactToWheelAsync();
        }
    }

    private async void OnOpenActivePigeonChatClicked(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedPigeonContact != null)
        {
            var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
            await Navigation.PushAsync(new ChatPage(chatVm, _viewModel.SelectedPigeonContact));
        }
        else
        {
            await PromptAddExistingContactToWheelAsync();
        }
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

    private async void OnShowMyQrClicked(object? sender, EventArgs e)
    {
        var card = _viewModel.GetMyQrCard();
        if (card == null)
        {
            await DisplayAlert("Erreur", "Profil non initialisé.", "OK");
            return;
        }

        await Navigation.PushModalAsync(new QrDisplayPage(card, _viewModel.Transport));
    }

    private async void OnAddContactClicked(object? sender, EventArgs e)
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
            await DisplayAlert("Demande transmise", $"Demande envoyée à {card.DisplayName} !", "OK");
        }));
    }

    private async void OnConversationSelected(object? sender, Contact selectedContact)
    {
        var chatVm = _serviceProvider.GetRequiredService<ChatViewModel>();
        await Navigation.PushAsync(new ChatPage(chatVm, selectedContact));
    }

    private async void OnOpenCampusBoardClicked(object? sender, EventArgs e)
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

    private async void OnChangeNameClicked(object? sender, EventArgs e)
    {
        string? newName = await DisplayPromptAsync(
            "Modifier mon pseudo",
            "Entrez votre nouveau nom d'utilisateur :",
            initialValue: _viewModel.DisplayName,
            maxLength: 30,
            keyboard: Keyboard.Text);

        if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != _viewModel.DisplayName)
        {
            await _viewModel.UpdateDisplayNameAsync(newName.Trim());
            await DisplayAlert("Profil mis à jour", $"Votre pseudo est désormais {newName.Trim()}.", "OK");
        }
    }

    private async void OnOpenDevDiagnosticsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new DevDiagnosticsPage(_viewModel));
    }
}
