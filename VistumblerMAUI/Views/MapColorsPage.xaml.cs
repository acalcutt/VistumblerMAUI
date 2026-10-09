using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class MapColorsPage : ContentPage
{
    public MapColorsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
