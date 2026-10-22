using TellMe.App.ViewModels;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.Views;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private readonly Contact _conversation;

    public ChatPage(ChatViewModel viewModel, Contact conversation)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _conversation = conversation;
        BindingContext = _viewModel;

        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.ScrollToItemRequested += OnScrollToItemRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync(_conversation);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _viewModel.ScrollToItemRequested -= OnScrollToItemRequested;
        _viewModel.Cleanup();
    }

    private void OnScrollToBottomRequested()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_viewModel.Messages.Count > 0)
            {
                var lastItem = _viewModel.Messages.Last();
                MessagesCollectionView.ScrollTo(lastItem, position: ScrollToPosition.End, animate: false);
            }
        });
    }

    private void OnScrollToItemRequested(ChatMessageItem targetItem)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MessagesCollectionView.ScrollTo(targetItem, position: ScrollToPosition.Start, animate: false);
        });
    }

    private async void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // Déclencher le chargement par paliers dès que l'utilisateur approche du sommet de l'historique
        if (e.FirstVisibleItemIndex <= 1 && !_viewModel.IsLoadingOlder && _viewModel.HasMoreOlderMessages)
        {
            await _viewModel.LoadOlderMessagesAsync();
        }
    }

    private async void OnSendClicked(object sender, EventArgs e)
    {
        await _viewModel.SendMessageAsync();
    }
}
