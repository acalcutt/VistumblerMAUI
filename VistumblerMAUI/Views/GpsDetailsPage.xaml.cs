using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class GpsDetailsPage : ContentPage
{
    private readonly GpsDetailsViewModel _vm;

    public GpsDetailsPage(GpsDetailsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        CompassView.Drawable = vm.Compass;
        vm.CompassUpdated += () => CompassView.Invalidate();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Stop();
    }
}
