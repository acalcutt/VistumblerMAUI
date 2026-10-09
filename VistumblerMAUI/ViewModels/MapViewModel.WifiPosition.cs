using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VistumblerMAUI.Services.M8b;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// The Wi-Fi position from the GPS details page drawn on the map: the 1 km MGRS squares the APs in range voted
/// for, the best one darkest. Opened with "//MapPage?m8b=SQUARE:votes,SQUARE:votes,…".
/// </summary>
public partial class MapViewModel
{
    private const string WifiSquares = "m8b-squares";
    private string? _wifiSquaresGeoJson;
    private (double Lat, double Lon)? _wifiSquaresCamera;
    private bool _wifiSquaresDrawn;

    [ObservableProperty] private string _wifiSquaresTitle = string.Empty;
    [ObservableProperty] private bool _hasWifiSquares;

    public void ShowWifiSquares(string query)
    {
        var squares = new List<(MgrsSquare Square, int Votes)>();
        foreach (var part in query.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && bits[0].Length == 9 && int.TryParse(bits[1], CultureInfo.InvariantCulture, out var votes))
                squares.Add((new MgrsSquare(bits[0]), votes));
        }
        if (squares.Count == 0) return;

        int top = squares.Max(s => s.Votes);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (var (square, votes) in squares)
        {
            (double Lat, double Lon)[] corners;
            try { corners = square.Corners(); }
            catch (Exception) { continue; }   // not a square we can place
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"type\":\"Feature\",\"properties\":{\"share\":").Append(((double)votes / top).ToString("0.###", inv))
              .Append("},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");
            foreach (var (lat, lon) in corners.Append(corners[0]))
                sb.Append('[').Append(lon.ToString("F7", inv)).Append(',').Append(lat.ToString("F7", inv)).Append("],");
            sb.Length--;
            sb.Append("]]}}");
        }
        sb.Append("]}");

        _wifiSquaresGeoJson = sb.ToString();
        _wifiSquaresCamera = squares[0].Square.Center();
        WifiSquaresTitle = squares.Count == 1
            ? $"Wi-Fi position: square {squares[0].Square}, {top} AP vote(s)"
            : $"Wi-Fi position: best square {squares[0].Square} ({top} votes), {squares.Count - 1} more shaded by votes";
        HasWifiSquares = true;
        StatusMessage = WifiSquaresTitle;
        DrawWifiSquares();
    }

    [RelayCommand]
    private void ClearWifiSquares()
    {
        RemoveWifiSquareLayers();
        _wifiSquaresGeoJson = null;
        _wifiSquaresCamera = null;
        HasWifiSquares = false;
        WifiSquaresTitle = string.Empty;
    }

    /// <summary>Draws the squares, if any. Also called when a style has loaded (OnMapControllerReady).</summary>
    private void DrawWifiSquares()
    {
        if (_controller is null || _wifiSquaresGeoJson is not { } geoJson) return;
        try
        {
            RemoveWifiSquareLayers();
            _controller.AddGeoJsonSource(WifiSquares, geoJson);
            _controller.AddFillLayer("m8b-squares-fill", WifiSquares, null, null, new Dictionary<string, object?>
            {
                ["fill-color"] = "#8e24aa",
                ["fill-opacity"] = new object[] { "*", 0.4, new object[] { "get", "share" } },
            });
            _controller.AddLineLayer("m8b-squares-line", WifiSquares, null, null, new Dictionary<string, object?>
            {
                ["line-color"] = "#6a1b9a", ["line-width"] = 2.0,
            });
            _wifiSquaresDrawn = true;

            if (_wifiSquaresCamera is { } cam)
            {
                _controller.EaseTo(cam.Lat, cam.Lon, 13, durationMs: 600);
                _wifiSquaresCamera = null;   // only once; later redraws keep the user's view
            }
        }
        catch (Exception ex)
        {
            Services.DebugLog.Write($"[WifiPosition] couldn't draw: {ex.Message}");   // style not ready; redrawn when it is
        }
    }

    private void RemoveWifiSquareLayers()
    {
        if (_controller is null || !_wifiSquaresDrawn) return;
        foreach (var layer in new[] { "m8b-squares-line", "m8b-squares-fill" })
            try { _controller.RemoveLayer(layer); } catch { /* already gone */ }
        try { _controller.RemoveSource(WifiSquares); } catch { /* already gone */ }
        _wifiSquaresDrawn = false;
    }
}
