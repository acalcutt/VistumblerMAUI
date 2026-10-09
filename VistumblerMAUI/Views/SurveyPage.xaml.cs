using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Views;

/// <summary>Site survey (tap-to-mark): the plan with pinch-to-zoom, drag-to-move and tap-to-measure.</summary>
public partial class SurveyPage : ContentPage
{
    private readonly SurveyViewModel _vm;
    private bool _fitPending = true;
    private double _panStartX, _panStartY;

    public SurveyPage(SurveyViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Canvas.Drawable = vm.Drawable;
        vm.Redraw += refit =>
        {
            if (refit) _fitPending = true;
            if (_fitPending) Fit();
            Canvas.Invalidate();
        };
        Canvas.SizeChanged += (_, _) => { if (_fitPending) { Fit(); Canvas.Invalidate(); } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(Canvas) is not { } p) return;
            var (x, y) = _vm.Drawable.ToPlan(p.X, p.Y);
            _ = _vm.TapAsync(x, y);
        };
        Canvas.GestureRecognizers.Add(tap);

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            if (e.Status != GestureStatus.Running) return;
            Zoom(e.Scale, e.ScaleOrigin.X * Canvas.Width, e.ScaleOrigin.Y * Canvas.Height);
        };
        Canvas.GestureRecognizers.Add(pinch);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            var d = _vm.Drawable;
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panStartX = d.OffsetX;
                    _panStartY = d.OffsetY;
                    break;
                case GestureStatus.Running:
                    d.OffsetX = _panStartX + e.TotalX;
                    d.OffsetY = _panStartY + e.TotalY;
                    _fitPending = false;
                    Canvas.Invalidate();
                    break;
            }
        };
        Canvas.GestureRecognizers.Add(pan);
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

    private void Fit()
    {
        if (Canvas.Width <= 0 || Canvas.Height <= 0) return;
        _vm.Drawable.Fit(Canvas.Width, Canvas.Height);
        _fitPending = false;
    }

    // Zooms by a factor about a point on the view, keeping that point still
    private void Zoom(double factor, double aboutX, double aboutY)
    {
        var d = _vm.Drawable;
        double scale = Math.Clamp(d.Scale * factor, 0.02, 50);
        factor = scale / d.Scale;
        d.OffsetX = aboutX - (aboutX - d.OffsetX) * factor;
        d.OffsetY = aboutY - (aboutY - d.OffsetY) * factor;
        d.Scale = scale;
        _fitPending = false;
        Canvas.Invalidate();
    }

    private void OnZoomIn(object? sender, EventArgs e) => Zoom(1.5, Canvas.Width / 2, Canvas.Height / 2);
    private void OnZoomOut(object? sender, EventArgs e) => Zoom(1 / 1.5, Canvas.Width / 2, Canvas.Height / 2);
    private void OnFit(object? sender, EventArgs e) { Fit(); Canvas.Invalidate(); }

    /// <summary>Saves the plan with its marks and heatmap as a PNG, fitted to the view.</summary>
    private async void OnSaveImage(object? sender, EventArgs e)
    {
        try
        {
            Fit();
            Canvas.Invalidate();
            await Task.Delay(150);   // let the fitted view draw before it's captured
            var shot = await Canvas.CaptureAsync();
            if (shot is null) return;
            await _vm.SaveAndShareAsync(_vm.FileNameFor(".png"), async path =>
            {
                await using var png = await shot.OpenReadAsync(ScreenshotFormat.Png);
                await using var file = File.Create(path);
                await png.CopyToAsync(file);
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Save image", $"Couldn't save the image: {ex.Message}", "OK");
        }
    }
}
