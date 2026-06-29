using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ExportPage : ContentPage
{
    public ExportPage(ExportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
