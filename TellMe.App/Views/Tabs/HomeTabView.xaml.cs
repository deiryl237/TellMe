using TellMe.App.ViewModels;
using TellMe.Core.Models;
using Contact = TellMe.Core.Models.Contact;

namespace TellMe.App.Views.Tabs;

public partial class HomeTabView : ContentView
{
    public event EventHandler? ShowMyQrRequested;
    public event EventHandler? ChangeNameRequested;
    public event EventHandler<int>? SlotSelected;
    public event EventHandler? AddSlotRequested;
    public event EventHandler? PebbleClicked;
    public event EventHandler? OpenActivePigeonChatRequested;
    public event EventHandler? RemoveContactFromWheelRequested;

    public HomeTabView()
    {
        InitializeComponent();
    }

    public void SetRadialSlots(int count, int selectedIndex)
    {
        PerchWheel.SetSlots(null, count, selectedIndex);
    }

    public void SetWheelContacts(IList<Contact> contacts, int selectedIndex)
    {
        PerchWheel.SetSlots(contacts, contacts.Count, selectedIndex);
    }

    public void UpdateStatusBadge(string badge, bool inFlight)
    {
        PerchWheel.SetStatusBadge(badge, inFlight);
    }

    private void OnShowMyQrClicked(object? sender, EventArgs e)
    {
        ShowMyQrRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWheelSlotSelected(object? sender, int slotIndex)
    {
        SlotSelected?.Invoke(this, slotIndex);
    }

    private void OnWheelAddSlotClicked(object? sender, EventArgs e)
    {
        AddSlotRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWheelPebbleClicked(object? sender, EventArgs e)
    {
        PebbleClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenActivePigeonChatClicked(object? sender, EventArgs e)
    {
        OpenActivePigeonChatRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoveContactFromWheelClicked(object? sender, EventArgs e)
    {
        RemoveContactFromWheelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditNameClicked(object? sender, EventArgs e)
    {
        ChangeNameRequested?.Invoke(this, EventArgs.Empty);
    }
}
