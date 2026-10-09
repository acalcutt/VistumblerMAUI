using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class FilterEditPage : ContentPage
{
    public FilterEditPage(FilterEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
