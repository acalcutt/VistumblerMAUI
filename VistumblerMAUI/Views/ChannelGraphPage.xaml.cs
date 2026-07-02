using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

public partial class ChannelGraphPage : ContentPage
{
    private readonly ChannelGraphViewModel _vm;
    private IDispatcherTimer? _timer;

    public ChannelGraphPage(ChannelGraphViewModel vm, ScanViewModel scan)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        ScanBar.BindingContext = scan;      // shared control bar

        GraphView.Drawable = _vm.Graph;
        _vm.GraphUpdated += () => MainThread.BeginInvokeOnMainThread(GraphView.Invalidate);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Refresh();

        // Refresh while visible so the graph tracks the live scan.
        _timer ??= CreateTimer();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private IDispatcherTimer CreateTimer()
    {
        var t = Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(2);
        t.Tick += (_, _) => _vm.Refresh();
        return t;
    }
}
