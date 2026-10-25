using System.Collections.ObjectModel;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.ViewModels;

public class ChatMessageItem : BaseViewModel
{
    private string _hash = string.Empty;
    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, value);
    }

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string SenderName { get; set; } = string.Empty;
    public bool IsMine { get; set; }
    public bool ShowDeliveryStatus { get; set; }
    public string TimeString { get; set; } = string.Empty;
    public long Timestamp { get; set; }

    private MessageDeliveryStatus _deliveryStatus = MessageDeliveryStatus.InFlight;
    public MessageDeliveryStatus DeliveryStatus
    {
        get => _deliveryStatus;
        set
        {
            if (SetProperty(ref _deliveryStatus, value))
            {
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(IsQueued));
                OnPropertyChanged(nameof(IsInFlight));
                OnPropertyChanged(nameof(IsDelivered));
            }
        }
    }

    public bool IsQueued => DeliveryStatus == MessageDeliveryStatus.Queued;
    public bool IsInFlight => DeliveryStatus == MessageDeliveryStatus.InFlight;
    public bool IsDelivered => DeliveryStatus == MessageDeliveryStatus.Delivered;

    public string StatusIcon => (!ShowDeliveryStatus) ? string.Empty : DeliveryStatus switch
    {
        MessageDeliveryStatus.Queued => "icon_radar.svg",
        MessageDeliveryStatus.InFlight => "icon_feather.svg",
        MessageDeliveryStatus.Delivered => "icon_check.svg",
        _ => string.Empty
    };

    public string StatusLabel => (!ShowDeliveryStatus) ? string.Empty : DeliveryStatus switch
    {
        MessageDeliveryStatus.Queued => "En attente au pigeonnier",
        MessageDeliveryStatus.InFlight => "En vol",
        MessageDeliveryStatus.Delivered => "Reçu",
        _ => string.Empty
    };
}

public class ChatViewModel : BaseViewModel
{
    private const int PageSize = 30;

    private readonly TellMeEngine _engine;
    private Contact? _targetConversation;

    private string _title = "Discussion";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _inputText = string.Empty;
    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    private bool _isLoadingOlder;
    public bool IsLoadingOlder
    {
        get => _isLoadingOlder;
        set => SetProperty(ref _isLoadingOlder, value);
    }

    private bool _hasMoreOlderMessages = true;
    public bool HasMoreOlderMessages
    {
        get => _hasMoreOlderMessages;
        set => SetProperty(ref _hasMoreOlderMessages, value);
    }

    public bool IsDirectContact => _targetConversation != null && _targetConversation.IsDirectContact;

    public bool IsPigeonInFlight => IsDirectContact && _targetConversation?.PigeonState == "InFlight";

    public int QueuedCount => Messages.Count(m => m.IsMine && m.DeliveryStatus == MessageDeliveryStatus.Queued);

    public string PigeonBannerTitle => $"Pigeon '{_targetConversation?.PigeonName ?? "Messager"}' en vol";

    public string PigeonBannerSubtitle
    {
        get
        {
            int q = QueuedCount;
            if (q > 0)
            {
                return $"{q} message(s) en attente au pigeonnier. Ils partiront groupés dès le retour du pigeon.";
            }
            return $"Il transporte votre message vers {_targetConversation?.DisplayName}. Vos prochains messages attendront son retour.";
        }
    }

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public event Action? ScrollToBottomRequested;
    public event Action<ChatMessageItem>? ScrollToItemRequested;

    public ChatViewModel(TellMeEngine engine)
    {
        _engine = engine;
    }

    private bool IsMessageForThisConversation(MeshMessage msg)
    {
        if (_targetConversation == null) return false;

        // 1. Cas d'un Board (Public ou Privé)
        if (_targetConversation.IsBoard)
        {
            return msg.To == _targetConversation.PublicKey;
        }

        // 2. Cas d'une discussion directe avec un ami
        var myId = _engine._myaccount?.Id;
        return (msg.To == _targetConversation.PublicKey && msg.From == myId)
            || (msg.From == _targetConversation.PublicKey && msg.To == myId);
    }

    public async Task InitializeAsync(Contact conversation)
    {
        _targetConversation = conversation;
        Title = conversation.DisplayName;
        HasMoreOlderMessages = true;

        // 1. Écouter les événements du moteur TellMe
        _engine._store.MessageAdded -= OnMessageAdded;
        _engine._store.MessageAdded += OnMessageAdded;

        _engine.PigeonTookOff -= OnPigeonTookOff;
        _engine.PigeonTookOff += OnPigeonTookOff;

        _engine.PigeonReturned -= OnPigeonReturned;
        _engine.PigeonReturned += OnPigeonReturned;

        _engine.MessageDelivered -= OnMessageDelivered;
        _engine.MessageDelivered += OnMessageDelivered;

        _engine.MessageQueued -= OnMessageQueued;
        _engine.MessageQueued += OnMessageQueued;

        UpdatePigeonState();

        // 2. Charger le dernier palier de messages de façon instantanée
        await LoadInitialMessagesAsync();
    }

    private async Task LoadInitialMessagesAsync()
    {
        if (_targetConversation == null) return;

        var myId = _engine._myaccount?.Id;
        var stored = await _engine._database.GetConversationMessagesPagedAsync(
            _targetConversation.PublicKey,
            _targetConversation.IsBoard,
            myId,
            limit: PageSize);

        if (stored.Count < PageSize)
        {
            HasMoreOlderMessages = false;
        }

        var items = new List<ChatMessageItem>();
        foreach (var s in stored)
        {
            if (s.IsAck) continue;
            var mesh = s.ToMeshMessage();
            long ts = mesh.Timestamp > 0 ? mesh.Timestamp : ((DateTimeOffset)s.ReceivedAt).ToUnixTimeMilliseconds();
            var item = await CreateMessageItemAsync(mesh, ts, s.DeliveryStatus);
            if (item != null)
            {
                items.Add(item);
            }
        }

        // Remettre dans l'ordre chronologique (du plus ancien au plus récent)
        items.Reverse();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Clear();
            foreach (var item in items)
            {
                Messages.Add(item);
            }
            UpdatePigeonState();
            ScrollToBottomRequested?.Invoke();
        });
    }

    /// <summary>
    /// Charge le palier précédent de messages plus anciens lorsque l'utilisateur scroll vers le haut.
    /// </summary>
    public async Task LoadOlderMessagesAsync()
    {
        if (IsLoadingOlder || !HasMoreOlderMessages || _targetConversation == null || Messages.Count == 0)
            return;

        IsLoadingOlder = true;

        try
        {
            long oldestTimestamp = Messages.First().Timestamp;
            var myId = _engine._myaccount?.Id;
            var stored = await _engine._database.GetConversationMessagesPagedAsync(
                _targetConversation.PublicKey,
                _targetConversation.IsBoard,
                myId,
                limit: PageSize,
                beforeTimestamp: oldestTimestamp);

            if (stored.Count < PageSize)
            {
                HasMoreOlderMessages = false;
            }

            if (stored.Count == 0)
            {
                return;
            }

            var items = new List<ChatMessageItem>();
            foreach (var s in stored)
            {
                if (s.IsAck) continue;
                var mesh = s.ToMeshMessage();
                long ts = mesh.Timestamp > 0 ? mesh.Timestamp : ((DateTimeOffset)s.ReceivedAt).ToUnixTimeMilliseconds();
                var item = await CreateMessageItemAsync(mesh, ts, s.DeliveryStatus);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            items.Reverse();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var previousTopItem = Messages.FirstOrDefault();

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (!Messages.Any(m => m.Hash == items[i].Hash))
                    {
                        Messages.Insert(0, items[i]);
                    }
                }

                if (previousTopItem != null)
                {
                    ScrollToItemRequested?.Invoke(previousTopItem);
                }
            });
        }
        finally
        {
            IsLoadingOlder = false;
        }
    }

    private async void OnMessageAdded(MeshMessage msg)
    {
        if (!IsMessageForThisConversation(msg)) return;

        bool isMine = _engine._myaccount != null && msg.From == _engine._myaccount.Id;
        var item = await CreateMessageItemAsync(msg, msg.Timestamp, isMine ? MessageDeliveryStatus.InFlight : MessageDeliveryStatus.Delivered);
        if (item == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Messages.Any(m => m.Hash == item.Hash)) return;

            if (Messages.Count == 0 || item.Timestamp >= Messages.Last().Timestamp)
            {
                Messages.Add(item);
                ScrollToBottomRequested?.Invoke();
            }
            else
            {
                int insertIndex = Messages.Count;
                for (int i = 0; i < Messages.Count; i++)
                {
                    if (Messages[i].Timestamp > item.Timestamp)
                    {
                        insertIndex = i;
                        break;
                    }
                }
                Messages.Insert(insertIndex, item);
            }

            UpdatePigeonState();
        });
    }

    private void OnMessageQueued(StoredMessage queued)
    {
        if (_targetConversation == null || queued.To != _targetConversation.PublicKey) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var existing = Messages.FirstOrDefault(m => m.Hash == queued.Hash);
            if (existing == null)
            {
                var item = await CreateMessageItemAsync(queued.ToMeshMessage(), queued.CreatedAt, MessageDeliveryStatus.Queued);
                if (item != null)
                {
                    Messages.Add(item);
                    ScrollToBottomRequested?.Invoke();
                }
            }
            UpdatePigeonState();
        });
    }

    private void OnPigeonTookOff(Contact contact, List<StoredMessage> msgs)
    {
        if (_targetConversation == null || contact.PublicKey != _targetConversation.PublicKey) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _targetConversation.PigeonState = "InFlight";
            _targetConversation.FlightStartedAt = contact.FlightStartedAt;
            _targetConversation.MessagesInFlightCount = contact.MessagesInFlightCount;

            // Passer tous les messages en attente en InFlight
            foreach (var m in Messages.Where(m => m.IsMine && m.DeliveryStatus == MessageDeliveryStatus.Queued))
            {
                m.DeliveryStatus = MessageDeliveryStatus.InFlight;
            }

            foreach (var msg in msgs)
            {
                var match = Messages.FirstOrDefault(m => m.Hash == msg.Hash);
                if (match != null)
                {
                    match.DeliveryStatus = MessageDeliveryStatus.InFlight;
                }
            }

            UpdatePigeonState();
        });
    }

    private void OnMessageDelivered(string targetHash)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var match = Messages.FirstOrDefault(m => m.Hash == targetHash);
            if (match != null)
            {
                match.DeliveryStatus = MessageDeliveryStatus.Delivered;
            }
            UpdatePigeonState();
        });
    }

    private void OnPigeonReturned(Contact contact)
    {
        if (_targetConversation == null || contact.PublicKey != _targetConversation.PublicKey) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _targetConversation.PigeonState = "AtPerch";
            _targetConversation.MessagesInFlightCount = 0;
            UpdatePigeonState();
        });
    }

    private void UpdatePigeonState()
    {
        OnPropertyChanged(nameof(IsDirectContact));
        OnPropertyChanged(nameof(IsPigeonInFlight));
        OnPropertyChanged(nameof(PigeonBannerTitle));
        OnPropertyChanged(nameof(PigeonBannerSubtitle));
        OnPropertyChanged(nameof(QueuedCount));
    }

    private async Task<ChatMessageItem?> CreateMessageItemAsync(MeshMessage msg, long timestamp = 0, MessageDeliveryStatus status = MessageDeliveryStatus.InFlight)
    {
        string text = await _engine.GetDecryptedContentAsync(msg);

        // Les messages d'accusé de réception technique ne doivent JAMAIS apparaître dans le chat
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("ACK:") || text.Contains("Accusé de réception"))
        {
            return null;
        }

        bool isMine = _engine._myaccount != null && msg.From == _engine._myaccount.Id;
        bool isDirect = _targetConversation != null && _targetConversation.IsDirectContact;

        string senderName;
        if (isMine)
        {
            senderName = "Moi";
        }
        else if (_targetConversation?.IsBoard == true)
        {
            var contact = await _engine._database.GetContactAsync(msg.From);
            senderName = contact?.DisplayName ?? (msg.From.Length > 6 ? msg.From[..6] : msg.From);
        }
        else
        {
            senderName = _targetConversation?.DisplayName ?? "Ami";
        }

        long ts = timestamp > 0 ? timestamp : (msg.Timestamp > 0 ? msg.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        DateTime displayTime = DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().DateTime;

        return new ChatMessageItem
        {
            Hash = msg.Hash,
            Text = text,
            SenderName = senderName,
            IsMine = isMine,
            ShowDeliveryStatus = isMine && isDirect,
            TimeString = displayTime.ToString("HH:mm"),
            Timestamp = ts,
            DeliveryStatus = isMine ? status : MessageDeliveryStatus.Delivered
        };
    }

    public void Cleanup()
    {
        _engine._store.MessageAdded -= OnMessageAdded;
        _engine.PigeonTookOff -= OnPigeonTookOff;
        _engine.PigeonReturned -= OnPigeonReturned;
        _engine.MessageDelivered -= OnMessageDelivered;
        _engine.MessageQueued -= OnMessageQueued;
    }

    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || _targetConversation == null)
            return;

        string textToSend = InputText.Trim();
        InputText = string.Empty;

        await _engine.SendMessageAsync(textToSend, _targetConversation.PublicKey);
    }
}
