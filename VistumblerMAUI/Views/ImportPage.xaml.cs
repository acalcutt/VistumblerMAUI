using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ImportPage : ContentPage
{
    public ImportPage(ImportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
