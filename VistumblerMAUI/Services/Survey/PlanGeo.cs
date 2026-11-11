namespace VistumblerMAUI.Services.Survey;

/// <summary>A latitude and longitude, in degrees.</summary>
public sealed class GeoPoint
{
    public double Lat { get; set; }
    public double Lon { get; set; }

    public GeoPoint() { }
    public GeoPoint(double lat, double lon) => (Lat, Lon) = (lat, lon);
}

/// <summary>
/// Where a survey's plan sits on the map: its four corners, as lined up on the map (or, later, read from a
/// georeferenced image). The plan's pixels map to the map by the parallelogram the top-left, top-right and
/// bottom-left corners span, which is exact for a plan lined up on a flat (unpitched) map at any rotation, and
/// close enough at building scale for the mercator projection's curvature not to matter.
/// </summary>
public sealed class PlanCorners
{
    public GeoPoint TopLeft { get; set; } = new();
    public GeoPoint TopRight { get; set; } = new();
    public GeoPoint BottomRight { get; set; } = new();
    public GeoPoint BottomLeft { get; set; } = new();

    /// <summary>The map position of a point on a plan of the given size, in plan units (pixels).</summary>
    public GeoPoint ToGeo(double planWidth, double planHeight, double x, double y)
    {
        double u = x / planWidth, v = y / planHeight;
        return new GeoPoint(
            TopLeft.Lat + u * (TopRight.Lat - TopLeft.Lat) + v * (BottomLeft.Lat - TopLeft.Lat),
            TopLeft.Lon + u * (TopRight.Lon - TopLeft.Lon) + v * (BottomLeft.Lon - TopLeft.Lon));
    }

    /// <summary>The plan position (pixels) of a map position; null if the corners are degenerate.</summary>
    public (double X, double Y)? ToPlan(double planWidth, double planHeight, double lat, double lon)
    {
        // Solve  p - TL = u (TR - TL) + v (BL - TL)  for u and v
        double ax = TopRight.Lon - TopLeft.Lon, ay = TopRight.Lat - TopLeft.Lat;
        double bx = BottomLeft.Lon - TopLeft.Lon, by = BottomLeft.Lat - TopLeft.Lat;
        double px = lon - TopLeft.Lon, py = lat - TopLeft.Lat;
        double det = ax * by - ay * bx;
        if (Math.Abs(det) < 1e-18) return null;
        double u = (px * by - py * bx) / det;
        double v = (ax * py - ay * px) / det;
        return (u * planWidth, v * planHeight);
    }

    /// <summary>The corners' bounding box: south, west, north, east.</summary>
    public (double South, double West, double North, double East) Bounds()
    {
        var all = new[] { TopLeft, TopRight, BottomRight, BottomLeft };
        return (all.Min(p => p.Lat), all.Min(p => p.Lon), all.Max(p => p.Lat), all.Max(p => p.Lon));
    }
}
