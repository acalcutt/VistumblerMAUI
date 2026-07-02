using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ApDetailsPage : ContentPage
{
    private readonly ApDetailsViewModel _vm;

    public ApDetailsPage(ApDetailsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        GraphView.Drawable = _vm.Graph;
        // Redraw the graph once the AP + its signal history have loaded.
        _vm.GraphUpdated += () => MainThread.BeginInvokeOnMainThread(GraphView.Invalidate);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
