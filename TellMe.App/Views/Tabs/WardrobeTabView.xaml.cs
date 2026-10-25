using TellMe.App.ViewModels;

namespace TellMe.App.Views.Tabs;

public partial class WardrobeTabView : ContentView
{
    public WardrobeTabView()
    {
        InitializeComponent();
    }

    private async void OnSavePigeonClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            string newName = PigeonNameEntry.Text?.Trim() ?? vm.PigeonName;
            string feather = (string?)FeatherPicker.SelectedItem ?? vm.FeatherColor;
            string accessory = (string?)AccessoryPicker.SelectedItem ?? vm.Accessory;

            await vm.SavePigeonProfileAsync(newName, feather, accessory);

            var page = Application.Current?.MainPage ?? Window?.Page;
            if (page != null)
            {
                await page.DisplayAlert("Garde-Robe", "Le look de votre pigeon voyageur a été mis à jour !", "OK");
            }
        }
    }
}
