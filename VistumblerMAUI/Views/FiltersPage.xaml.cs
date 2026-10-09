using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class FiltersPage : ContentPage
{
    private readonly FiltersViewModel _vm;

    public FiltersPage(FiltersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Pick up filters added or edited on the edit page
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Refresh();
    }
}
