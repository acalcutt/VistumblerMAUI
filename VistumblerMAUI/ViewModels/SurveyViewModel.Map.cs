using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Controls;
using VistumblerMAUI.Localization;
using VistumblerMAUI.Services.Survey;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// The survey over the map: the basemap from the Map tab, the plan image laid on it where it's been lined up, and
/// the spots at their map positions. A plan image is lined up by holding it still over the middle of the map and
/// moving, zooming and rotating the map under it until they match (<see cref="StartPlacing"/>); the page then
/// reads the map positions under the image's corners (<see cref="PinPlan"/>).
/// </summary>
public partial class SurveyViewModel
{
    /// <summary>Over the map (true) or the plan alone (false).</summary>
    [ObservableProperty] private bool _isMapView;

    /// <summary>Lining the plan image up on the map: the image held over the map, Pin here / Cancel shown.</summary>
    [ObservableProperty] private bool _isPlacingPlan;

    /// <summary>Raised when the plan image or where it sits on the map changed, so the map redraws it.</summary>
    public event Action? PlanChanged;

    public string? PlanImagePath => _survey.PlanImagePath is { } p && File.Exists(p) ? p : null;
    public PlanCorners? PlanCorners => _survey.Corners;
    public double PlanWidth => _survey.PlanWidth;
    public double PlanHeight => _survey.PlanHeight;

    partial void OnIsMapViewChanged(bool value)
    {
        if (!value) IsPlacingPlan = false;
        if (_survey.OnMap == value) return;
        _survey.OnMap = value;
        Save();
    }

    [RelayCommand]
    private void ToggleMapView() => IsMapView = !IsMapView;

    /// <summary>Starts lining the plan image up on the map. Needs an image: the grid has nothing to line up.</summary>
    [RelayCommand]
    private void StartPlacing()
    {
        IsMenuOpen = false;
        if (PlanImagePath is null)
        {
            Status = Loc.T("Survey_PlaceNeedsImage");
            return;
        }
        IsMapView = true;
        IsPlacingPlan = true;
        Status = Loc.T("Survey_PlaceHint");
        PlanChanged?.Invoke();   // the lined-up image comes off the map while it's being placed again
    }

    /// <summary>Keeps where the image's corners fell on the map.</summary>
    public void PinPlan(PlanCorners corners)
    {
        _survey.Corners = corners;
        IsPlacingPlan = false;
        Save();
        ShowPending();
        Refresh(refit: false);
        PlanChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelPlacing()
    {
        IsPlacingPlan = false;
        UpdateStatus();
        PlanChanged?.Invoke();
    }

    // ── Where a spot is, on the plan and on the map ──────────────────────────

    /// <summary>A spot's plan position: where it was tapped on the plan, or its map position on a lined-up plan.</summary>
    private (double X, double Y)? PlanOf(SurveyMark m)
    {
        if (m.OnPlan) return (m.X, m.Y);
        if (m.Latitude is { } lat && m.Longitude is { } lon && _survey.Corners is { } c)
            return c.ToPlan(_survey.PlanWidth, _survey.PlanHeight, lat, lon);
        return null;
    }

    /// <summary>A spot's map position: where it was tapped on the map, or its plan position on a lined-up plan.</summary>
    private GeoPoint? GeoOf(SurveyMark m)
    {
        if (!m.OnPlan && m.Latitude is { } lat && m.Longitude is { } lon) return new GeoPoint(lat, lon);
        if (m.OnPlan && _survey.Corners is { } c) return c.ToGeo(_survey.PlanWidth, _survey.PlanHeight, m.X, m.Y);
        return null;
    }

    private GeoPoint? PendingGeo => _pending switch
    {
        { Lat: { } lat, Lon: { } lon } => new GeoPoint(lat, lon),
        { X: { } x, Y: { } y } when _survey.Corners is { } c => c.ToGeo(_survey.PlanWidth, _survey.PlanHeight, x, y),
        _ => null,
    };

    /// <summary>
    /// The spots with a map position, as GeoJSON points for the map's circle layer: each with its index in the
    /// survey (to find it again from a tap), its colour for the chosen network's RSSI, and whether it heard it.
    /// The spot being measured, if any, is a point with <c>pending</c> set.
    /// </summary>
    public string SpotsGeoJson()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        void Point(GeoPoint p, string properties)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[")
              .Append(p.Lon.ToString("R", inv)).Append(',').Append(p.Lat.ToString("R", inv))
              .Append("]},\"properties\":{").Append(properties).Append("}}");
        }
        for (int i = 0; i < _survey.Marks.Count; i++)
        {
            if (GeoOf(_survey.Marks[i]) is not { } geo) continue;
            var rssi = ValueAt(_survey.Marks[i]);
            var color = rssi is { } r ? Hex(SurveyDrawable.RssiColor(r)) : "#464646";
            Point(geo, $"\"index\":{i},\"color\":\"{color}\",\"heard\":{(rssi is null ? "false" : "true")}");
        }
        if (PendingGeo is { } pending) Point(pending, "\"index\":-1,\"pending\":true,\"color\":\"#1565C0\"");
        return sb.Append("]}").ToString();
    }

    private static string Hex(Color c) => $"#{(int)(c.Red * 255):X2}{(int)(c.Green * 255):X2}{(int)(c.Blue * 255):X2}";

    /// <summary>Where the map view should look first: the plan's corners, else the spots; null when neither.</summary>
    public (double South, double West, double North, double East)? MapExtent()
    {
        if (_survey.Corners is { } c) return c.Bounds();
        var spots = _survey.Marks.Select(GeoOf).OfType<GeoPoint>().ToList();
        if (spots.Count == 0) return null;
        return (spots.Min(p => p.Lat), spots.Min(p => p.Lon), spots.Max(p => p.Lat), spots.Max(p => p.Lon));
    }
}
