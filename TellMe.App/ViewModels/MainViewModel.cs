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

    private string _statusText = "Pigeonnier actif • À l'écoute";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ObservableCollection<string> DevLogs { get; } = new();

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

    // La vitrine des contacts épinglés sur la roue du perchoir
    public ObservableCollection<Contact> WheelContacts { get; } = new();

    private const string PinnedWheelPrefKey = "tellme_pinned_wheel_keys";

    public bool IsSelectedContactOnWheel => _selectedPigeonContact != null && WheelContacts.Any(c => c.PublicKey == _selectedPigeonContact.PublicKey);

    public void LoadWheelContacts()
    {
        WheelContacts.Clear();
        string saved = Preferences.Default.Get<string>(PinnedWheelPrefKey, string.Empty);
        List<string> pinnedKeys;
        if (!string.IsNullOrEmpty(saved))
        {
            try
            {
                pinnedKeys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(saved) ?? new List<string>();
            }
            catch
            {
                pinnedKeys = new List<string>();
            }
        }
        else
        {
            // Par défaut lors de la première installation : épingler les 3 premiers contacts
            pinnedKeys = PrivateChats.Take(3).Select(c => c.PublicKey).ToList();
            SaveWheelContacts(pinnedKeys);
        }

        foreach (var key in pinnedKeys)
        {
            var match = PrivateChats.FirstOrDefault(c => c.PublicKey == key);
            if (match != null && !WheelContacts.Any(w => w.PublicKey == match.PublicKey))
            {
                WheelContacts.Add(match);
            }
        }

        if (SelectedPigeonContact == null && WheelContacts.Count > 0)
        {
            SelectedPigeonContact = WheelContacts[0];
        }
        RefreshSelectedPigeon();
    }

    private void SaveWheelContacts(IEnumerable<string> keys)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(keys.ToList());
            Preferences.Default.Set(PinnedWheelPrefKey, json);
        }
        catch { }
    }

    public bool AddContactToWheel(Contact contact)
    {
        if (contact == null) return false;
        if (WheelContacts.Count >= 8) return false;
        if (WheelContacts.Any(c => c.PublicKey == contact.PublicKey)) return false;

        WheelContacts.Add(contact);
        SaveWheelContacts(WheelContacts.Select(c => c.PublicKey));
        SelectedPigeonContact = contact;
        RefreshSelectedPigeon();
        return true;
    }

    public bool RemoveContactFromWheel(Contact contact)
    {
        if (contact == null) return false;
        var existing = WheelContacts.FirstOrDefault(c => c.PublicKey == contact.PublicKey);
        if (existing == null) return false;

        int index = WheelContacts.IndexOf(existing);
        WheelContacts.Remove(existing);
        SaveWheelContacts(WheelContacts.Select(c => c.PublicKey));

        if (SelectedPigeonContact?.PublicKey == contact.PublicKey)
        {
            if (WheelContacts.Count > 0)
            {
                int nextIndex = Math.Clamp(index, 0, WheelContacts.Count - 1);
                SelectedPigeonContact = WheelContacts[nextIndex];
            }
            else
            {
                SelectedPigeonContact = null;
            }
        }

        RefreshSelectedPigeon();
        return true;
    }

    private Contact? _selectedPigeonContact;
    public Contact? SelectedPigeonContact
    {
        get => _selectedPigeonContact;
        set
        {
            if (SetProperty(ref _selectedPigeonContact, value))
            {
                RefreshSelectedPigeon();
            }
        }
    }

    public void RefreshSelectedPigeon()
    {
        OnPropertyChanged(nameof(ActivePigeonName));
        OnPropertyChanged(nameof(ActivePigeonSubtitle));
        OnPropertyChanged(nameof(IsActivePigeonInFlight));
        OnPropertyChanged(nameof(ActivePigeonStatusBadge));
        OnPropertyChanged(nameof(ActivePigeonMissionText));
        OnPropertyChanged(nameof(ActivePigeonActionText));
        OnPropertyChanged(nameof(ActivePigeonInitial));
        OnPropertyChanged(nameof(IsSelectedContactOnWheel));
    }

    public string ActivePigeonName => _selectedPigeonContact?.PigeonName ?? (string.IsNullOrEmpty(PigeonName) ? "AileRapide" : PigeonName);

    public string ActivePigeonSubtitle => _selectedPigeonContact != null
        ? $"Pigeon dédié à {_selectedPigeonContact.DisplayName}"
        : "Mon Pigeon Messager Principal";

    public bool IsActivePigeonInFlight => _selectedPigeonContact?.PigeonState == "InFlight";

    public string ActivePigeonStatusBadge => IsActivePigeonInFlight ? " EN VOL" : " AU PERCHOIR";

    public string ActivePigeonInitial => (!string.IsNullOrEmpty(ActivePigeonName))
        ? ActivePigeonName[0].ToString().ToUpperInvariant()
        : "P";

    public string ActivePigeonMissionText
    {
        get
        {
            if (_selectedPigeonContact == null)
            {
                return "Votre pigeonnier est prêt. Ajoutez des amis via QR code pour déployer votre escouade de pigeons messagers !";
            }

            if (IsActivePigeonInFlight)
            {
                int count = _selectedPigeonContact.MessagesInFlightCount > 0 ? _selectedPigeonContact.MessagesInFlightCount : 1;
                return $"En vol vers {_selectedPigeonContact.DisplayName} • {count} message(s) à bord. Les nouveaux messages patientent au perchoir.";
            }

            return $"Au perchoir • Prêt à s'envoler pour livrer vos messages à {_selectedPigeonContact.DisplayName}.";
        }
    }

    public string ActivePigeonActionText => _selectedPigeonContact != null
        ? $"Écrire à {_selectedPigeonContact.DisplayName}"
        : "Ajouter un contact sur la roue";

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

        // 3. Quand un pigeon prend son envol
        _engine.PigeonTookOff += (contact, msgs) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var match = PrivateChats.FirstOrDefault(c => c.PublicKey == contact.PublicKey);
                if (match != null)
                {
                    match.PigeonState = "InFlight";
                    match.FlightStartedAt = contact.FlightStartedAt;
                    match.MessagesInFlightCount = contact.MessagesInFlightCount;
                }
                if (_selectedPigeonContact?.PublicKey == contact.PublicKey)
                {
                    _selectedPigeonContact.PigeonState = "InFlight";
                    _selectedPigeonContact.MessagesInFlightCount = contact.MessagesInFlightCount;
                    RefreshSelectedPigeon();
                }
            });
        };

        // 4. Quand un pigeon revient au perchoir
        _engine.PigeonReturned += (contact) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var match = PrivateChats.FirstOrDefault(c => c.PublicKey == contact.PublicKey);
                if (match != null)
                {
                    match.PigeonState = "AtPerch";
                    match.MessagesInFlightCount = 0;
                }
                if (_selectedPigeonContact?.PublicKey == contact.PublicKey)
                {
                    _selectedPigeonContact.PigeonState = "AtPerch";
                    _selectedPigeonContact.MessagesInFlightCount = 0;
                    RefreshSelectedPigeon();
                }
            });
        };

        #if ANDROID
        if (_engine._transport is TellMe.App.Platforms.Android.AndroidBluetoothTransport androidTransport)
        {
            androidTransport.StatusChanged += status =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DevLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {status}");
                    if (DevLogs.Count > 100) DevLogs.RemoveAt(DevLogs.Count - 1);

                    if (status.Contains("désactivé"))
                    {
                        StatusText = "Bluetooth désactivé";
                    }
                    else if (status.Contains("connecté") || status.Contains("Connecté") || status.Contains("Flash Sync"))
                    {
                        StatusText = "Pigeon connecté • Synchronisation";
                    }
                    else if (status.Contains("déconnecté"))
                    {
                        StatusText = "Pigeonnier prêt • En attente de pairs";
                    }
                    else if (status.Contains("Envoyé"))
                    {
                        StatusText = "Message envoyé avec succès";
                    }
                    else if (status.Contains("Paquet"))
                    {
                        StatusText = "Échange mesh en cours...";
                    }
                    else if (status.Contains("Pigeonnier ouvert") || status.Contains("En attente"))
                    {
                        StatusText = "Pigeonnier actif • À l'écoute";
                    }
                });
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

            LoadWheelContacts();

            if (SelectedPigeonContact == null && WheelContacts.Count > 0)
            {
                SelectedPigeonContact = WheelContacts[0];
            }
            else if (SelectedPigeonContact == null && privateList.Count > 0)
            {
                SelectedPigeonContact = privateList[0];
            }
            else if (SelectedPigeonContact != null)
            {
                var refreshed = privateList.FirstOrDefault(c => c.PublicKey == SelectedPigeonContact.PublicKey);
                if (refreshed != null)
                {
                    SelectedPigeonContact = refreshed;
                }
            }
            RefreshSelectedPigeon();
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