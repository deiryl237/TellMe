using TellMe.App.ViewModels;

namespace TellMe.App.Views;

public partial class DevDiagnosticsPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public DevDiagnosticsPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void OnClearLogsClicked(object? sender, EventArgs e)
    {
        _viewModel.DevLogs.Clear();
    }

    private async void OnForceSyncClicked(object? sender, EventArgs e)
    {
        await _viewModel.Transport.StartAsync();
        _viewModel.DevLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Rotation Flash Sync demandée manuellement.");
    }
}
