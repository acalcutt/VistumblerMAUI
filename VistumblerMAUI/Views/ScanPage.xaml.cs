using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ScanPage : ContentPage
{
    public ScanPage(ScanViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
