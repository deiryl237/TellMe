using System.Collections.ObjectModel;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using TellMe.Core.Network;
using Contact = TellMe.Core.Models.Contact;


namespace TellMe.App.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly TellMeEngine _engine;
    public ITransport Transport => _engine._transport;
        private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private string _pigeonName = string.Empty;
    public string PigeonName
    {
        get => _pigeonName;
        set => SetProperty(ref _pigeonName, value);
    }

    private string _publicKey = string.Empty;
    public string PublicKey
    {
        get => _publicKey;
        set => SetProperty(ref _publicKey, value);
    }

    private string _statusText = "Prêt";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private int _selectedTab = 0;
    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private string _featherColor = "Bleu";
    public string FeatherColor
    {
        get => _featherColor;
        set => SetProperty(ref _featherColor, value);
    }

    private string _accessory = "Aucun";
    public string Accessory
    {
        get => _accessory;
        set => SetProperty(ref _accessory, value);
    }

    private int _experiencePoints = 0;
    public int ExperiencePoints
    {
        get => _experiencePoints;
        set
        {
            if (SetProperty(ref _experiencePoints, value))
            {
                OnPropertyChanged(nameof(PigeonRank));
            }
        }
    }

    public string PigeonRank => ExperiencePoints switch
    {
        < 50 => "Pigeon Messager Novice",
        < 200 => "Éclaireur des Toits",
        < 500 => "As du Maillage Campus",
        _ => "Légende Ailée du Ciel"
    };

    private Contact? _campusContact;
    public Contact? CampusContact
    {
        get => _campusContact;
        set => SetProperty(ref _campusContact, value);
    }

    // La liste complète des conversations
    public ObservableCollection<Contact> Conversations { get; } = new();

    // La liste filtrée des amis privés
    public ObservableCollection<Contact> PrivateChats { get; } = new();

        // Événement pour demander à la vue (Page XAML) d'afficher le dialogue Accepter/Refuser
    public event Func<ContactRequest, Task<bool>>? ContactRequestPromptRequested;

    private readonly HashSet<string> _activePrompts = new();

    public MainViewModel(TellMeEngine engine)
    {
        _engine = engine;

        // 1. Quand un ami nous demande en contact
        _engine._syncEngine.ContactRequestReceived += async (request) =>
        {
            lock (_activePrompts)
            {
                if (_activePrompts.Contains(request.RequestHash)) return;
                _activePrompts.Add(request.RequestHash);
            }

            if (ContactRequestPromptRequested != null)
            {
                bool accepted = await ContactRequestPromptRequested.Invoke(request);
                if (accepted)
                {
                    await _engine.AcceptContactRequestAsync(request);
                }
            }
        };

        // 2. Dès qu'un nouveau contact est enregistré en base (accepté par nous ou par l'ami)
        _engine.ContactAdded += async (contact) =>
        {
            await LoadConversationsAsync();
        };

        #if ANDROID
        if (_engine._transport is TellMe.App.Platforms.Android.AndroidBluetoothTransport androidTransport)
        {
            androidTransport.StatusChanged += status =>
            {
                MainThread.BeginInvokeOnMainThread(() => StatusText = status);
            };
        }
        #endif
    }

    private bool _isInitialized = false;

    public async Task InitializeAsync(string defaultUserName = "Moi")
    {
        if (!_isInitialized)
        {
            await _engine.InitializeAsync(defaultUserName);
            _isInitialized = true;

            if (_engine._myaccount != null)
            {
                DisplayName = _engine._myaccount.DisplayName;
                PigeonName = _engine._myaccount.PigeonName;
                PublicKey = _engine._myaccount.Id;
                FeatherColor = _engine._myaccount.FeatherColor ?? "Bleu";
                Accessory = _engine._myaccount.Accessory ?? "Aucun";
                ExperiencePoints = _engine._myaccount.ExperiencePoints;
            }
        }

        await LoadConversationsAsync();
    }

    public async Task LoadConversationsAsync()
    {
        var all = await _engine._database.GetAllContactsAsync();

        // Par défaut, assurons-nous que le board public du campus existe !
        var campus = all.FirstOrDefault(c => c.PublicKey == "PUBLICBOARD:Campus");
        if (campus == null)
        {
            campus = new Contact("PUBLICBOARD:Campus", "Campus");
            await _engine._database.SaveContactAsync(campus);
            all.Add(campus);
        }

        var privateList = all.Where(c => !c.PublicKey.StartsWith("PUBLICBOARD:")).ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            CampusContact = campus;

            Conversations.Clear();
            foreach (var c in all)
            {
                Conversations.Add(c);
            }

            PrivateChats.Clear();
            foreach (var c in privateList)
            {
                PrivateChats.Add(c);
            }
        });
    }

    public async Task AddContactFromQrAsync(ContactQrCard card)
    {
        await _engine.AddContactFromScannedCardAsync(card);
        StatusText = $"Demande envoyée à {card.DisplayName}...";
    }

    public ContactQrCard? GetMyQrCard(string bluetoothAddress = "00:00:00:00:00:00")
    {
        if (_engine._myaccount == null) return null;
#if ANDROID
        var adapter = global::Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
        var address = adapter?.Name ?? bluetoothAddress;
#else
        var address = bluetoothAddress;
#endif
        return new ContactQrCard(_engine._myaccount, address);
    }

    public async Task UpdateDisplayNameAsync(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || _engine._myaccount == null) return;
        _engine._myaccount.DisplayName = newName.Trim();
        await _engine._database.SaveAccountAsync(_engine._myaccount);
        DisplayName = _engine._myaccount.DisplayName;
    }

    public async Task SavePigeonProfileAsync(string newPigeonName, string featherColor, string accessory)
    {
        if (_engine._myaccount == null) return;

        if (!string.IsNullOrWhiteSpace(newPigeonName))
        {
            _engine._myaccount.PigeonName = newPigeonName.Trim();
            PigeonName = _engine._myaccount.PigeonName;
        }

        _engine._myaccount.FeatherColor = featherColor;
        FeatherColor = featherColor;

        _engine._myaccount.Accessory = accessory;
        Accessory = accessory;

        await _engine._database.SaveAccountAsync(_engine._myaccount);
    }
}