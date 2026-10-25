namespace TellMe.App.Views.Tabs;

public partial class CampusTabView : ContentView
{
    public event EventHandler? OpenCampusBoardRequested;

    public CampusTabView()
    {
        InitializeComponent();
    }

    private void OnOpenCampusBoardClicked(object? sender, EventArgs e)
    {
        OpenCampusBoardRequested?.Invoke(this, EventArgs.Empty);
    }
}
