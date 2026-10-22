namespace TellMe.App.Controls;

public class RiveBottomBarView : View
{
    public static readonly BindableProperty SelectedTabProperty =
        BindableProperty.Create(
            nameof(SelectedTab),
            typeof(int),
            typeof(RiveBottomBarView),
            0);

    public int SelectedTab
    {
        get => (int)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public const string FireTab = "FireTab";
}
