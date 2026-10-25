using System.Windows.Input;

namespace TellMe.App.Controls;

public class RiveWheelView : View
{
    public static readonly BindableProperty SelectedSlotProperty =
        BindableProperty.Create(
            nameof(SelectedSlot),
            typeof(int),
            typeof(RiveWheelView),
            0,
            BindingMode.TwoWay);

    public int SelectedSlot
    {
        get => (int)GetValue(SelectedSlotProperty);
        set => SetValue(SelectedSlotProperty, value);
    }

    public static readonly BindableProperty SlotCountProperty =
        BindableProperty.Create(
            nameof(SlotCount),
            typeof(int),
            typeof(RiveWheelView),
            3,
            BindingMode.TwoWay);

    public int SlotCount
    {
        get => (int)GetValue(SlotCountProperty);
        set => SetValue(SlotCountProperty, value);
    }

    public static readonly BindableProperty SlotSelectedCommandProperty =
        BindableProperty.Create(
            nameof(SlotSelectedCommand),
            typeof(ICommand),
            typeof(RiveWheelView),
            null);

    public ICommand? SlotSelectedCommand
    {
        get => (ICommand?)GetValue(SlotSelectedCommandProperty);
        set => SetValue(SlotSelectedCommandProperty, value);
    }

    public static readonly BindableProperty AddSlotCommandProperty =
        BindableProperty.Create(
            nameof(AddSlotCommand),
            typeof(ICommand),
            typeof(RiveWheelView),
            null);

    public ICommand? AddSlotCommand
    {
        get => (ICommand?)GetValue(AddSlotCommandProperty);
        set => SetValue(AddSlotCommandProperty, value);
    }

    public static readonly BindableProperty PebbleCommandProperty =
        BindableProperty.Create(
            nameof(PebbleCommand),
            typeof(ICommand),
            typeof(RiveWheelView),
            null);

    public ICommand? PebbleCommand
    {
        get => (ICommand?)GetValue(PebbleCommandProperty);
        set => SetValue(PebbleCommandProperty, value);
    }

    public event EventHandler<int>? SlotSelected;
    public event EventHandler? AddSlotClicked;
    public event EventHandler? PebbleClicked;

    public void NotifySlotSelected(int slotIndex)
    {
        SelectedSlot = slotIndex;
        SlotSelected?.Invoke(this, slotIndex);
        if (SlotSelectedCommand?.CanExecute(slotIndex) == true)
        {
            SlotSelectedCommand.Execute(slotIndex);
        }
    }

    public void NotifyAddSlotClicked()
    {
        AddSlotClicked?.Invoke(this, EventArgs.Empty);
        if (AddSlotCommand?.CanExecute(null) == true)
        {
            AddSlotCommand.Execute(null);
        }
    }

    public void NotifyPebbleClicked()
    {
        PebbleClicked?.Invoke(this, EventArgs.Empty);
        if (PebbleCommand?.CanExecute(null) == true)
        {
            PebbleCommand.Execute(null);
        }
    }

    public const string FireAddSlot = "FireAddSlot";
    public const string FireResetWheel = "FireResetWheel";
}
