using System.Windows.Input;

namespace TellMe.App.Controls;

public partial class RiveBottomBar : ContentView
{
    public static readonly BindableProperty SelectedTabProperty =
        BindableProperty.Create(
            nameof(SelectedTab),
            typeof(int),
            typeof(RiveBottomBar),
            0,
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSelectedTabChanged);

    public int SelectedTab
    {
        get => (int)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public static readonly BindableProperty TabChangedCommandProperty =
        BindableProperty.Create(
            nameof(TabChangedCommand),
            typeof(ICommand),
            typeof(RiveBottomBar),
            null);

    public ICommand? TabChangedCommand
    {
        get => (ICommand?)GetValue(TabChangedCommandProperty);
        set => SetValue(TabChangedCommandProperty, value);
    }

    public event EventHandler<int>? TabChanged;

    public RiveBottomBar()
    {
        InitializeComponent();
    }

    private static void OnSelectedTabChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is RiveBottomBar bar && newValue is int newIndex)
        {
            if (bar.RiveView != null && bar.RiveView.SelectedTab != newIndex)
            {
                bar.RiveView.SelectedTab = newIndex;
            }
        }
    }

    public void SelectTab(int index)
    {
        if (SelectedTab == index)
            return;

        SelectedTab = index;

        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Ignore on platforms without haptic engine
        }

        TabChanged?.Invoke(this, index);
        TabChangedCommand?.Execute(index);
    }

    private void OnTab0Tapped(object sender, TappedEventArgs e) => SelectTab(0);
    private void OnTab1Tapped(object sender, TappedEventArgs e) => SelectTab(1);
    private void OnTab2Tapped(object sender, TappedEventArgs e) => SelectTab(2);
    private void OnTab3Tapped(object sender, TappedEventArgs e) => SelectTab(3);
}
