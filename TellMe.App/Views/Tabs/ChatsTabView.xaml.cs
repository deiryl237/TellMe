using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.Views.Tabs;

public partial class ChatsTabView : ContentView
{
    public event EventHandler? AddContactRequested;
    public event EventHandler<Contact>? ConversationSelected;

    public ChatsTabView()
    {
        InitializeComponent();
    }

    private void OnAddContactClicked(object? sender, EventArgs e)
    {
        AddContactRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnConversationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Contact contact)
        {
            ConversationSelected?.Invoke(this, contact);
        }
    }
}
