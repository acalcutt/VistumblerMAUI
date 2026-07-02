using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class SessionChooserPage : ContentPage
{
    public SessionChooserPage(SessionChooserViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
