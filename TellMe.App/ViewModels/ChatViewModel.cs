using System.Collections.ObjectModel;
using TellMe.Core.Engine;
using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.ViewModels;


public class ChatMessageItem
{
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public bool IsMine { get; set; }
    public string TimeString { get; set; } = string.Empty;
    public long Timestamp { get; set; }
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

        // 1. Écouter les nouveaux messages entrants
        _engine._store.MessageAdded -= OnMessageAdded;
        _engine._store.MessageAdded += OnMessageAdded;

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
            var mesh = s.ToMeshMessage();
            long ts = mesh.Timestamp > 0 ? mesh.Timestamp : ((DateTimeOffset)s.ReceivedAt).ToUnixTimeMilliseconds();
            var item = await CreateMessageItemAsync(mesh, ts);
            items.Add(item);
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
                var mesh = s.ToMeshMessage();
                long ts = mesh.Timestamp > 0 ? mesh.Timestamp : ((DateTimeOffset)s.ReceivedAt).ToUnixTimeMilliseconds();
                var item = await CreateMessageItemAsync(mesh, ts);
                items.Add(item);
            }

            // stored était DESC (du plus récent parmi les anciens au plus vieux).
            // items.Reverse() met les plus anciens en premier.
            items.Reverse();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var previousTopItem = Messages.FirstOrDefault();

                // Insérer les messages plus anciens au tout début de la liste
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (!Messages.Any(m => m.Hash == items[i].Hash))
                    {
                        Messages.Insert(0, items[i]);
                    }
                }

                // Stabiliser la vue sur l'élément où l'utilisateur se trouvait
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

        var item = await CreateMessageItemAsync(msg, msg.Timestamp);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Éviter les doublons visuels
            if (Messages.Any(m => m.Hash == item.Hash)) return;

            // Si c'est le message le plus récent, on l'ajoute à la fin et on scroll en bas
            if (Messages.Count == 0 || item.Timestamp >= Messages.Last().Timestamp)
            {
                Messages.Add(item);
                ScrollToBottomRequested?.Invoke();
            }
            else
            {
                // Insérer à la position chronologique exacte
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
        });
    }

    private async Task<ChatMessageItem> CreateMessageItemAsync(MeshMessage msg, long timestamp = 0)
    {
        bool isMine = _engine._myaccount != null && msg.From == _engine._myaccount.Id;
        string text = await _engine.GetDecryptedContentAsync(msg);

        string senderName;
        if (isMine)
        {
            senderName = "Moi";
        }
        else if (_targetConversation?.IsBoard == true)
        {
            // Sur un board public, on essaie de trouver le nom du contact, ou on affiche le début de sa clé
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
            TimeString = displayTime.ToString("HH:mm"),
            Timestamp = ts
        };
    }

    public void Cleanup()
    {
        _engine._store.MessageAdded -= OnMessageAdded;
    }

    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || _targetConversation == null)
            return;

        string textToSend = InputText.Trim();
        InputText = string.Empty; // Vide le champ immédiatement pour la réactivité UI

        await _engine.SendMessageAsync(textToSend, _targetConversation.PublicKey);
    }
}
