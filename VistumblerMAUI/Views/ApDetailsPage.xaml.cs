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
        // Redraw the graph whenever the AP + its signal history have been refreshed.
        _vm.GraphUpdated += () => MainThread.BeginInvokeOnMainThread(GraphView.Invalidate);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartLiveUpdates();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopLiveUpdates();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
