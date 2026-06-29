using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class AccessPointListPage : ContentPage
{
    private readonly AccessPointListViewModel _vm;

    public AccessPointListPage(AccessPointListViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadCommand.ExecuteAsync(null);
    }
}
