using Microsoft.Maui.Graphics;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace VistumblerMAUI.Controls;

/// <summary>
/// Draws a site survey: the plan (image or grid), a heatmap interpolated between the marks, and the marks
/// themselves, coloured by RSSI with WifiDB's signal-map ramp. Plan units map to the screen by
/// <see cref="Scale"/> and <see cref="OffsetX"/>/<see cref="OffsetY"/>, which the page's pinch and pan change.
/// </summary>
public sealed class SurveyDrawable : IDrawable
{
    public IImage? PlanImage { get; set; }
    public double PlanWidth { get; set; } = 1000;
    public double PlanHeight { get; set; } = 1000;

    /// <summary>Each mark's position and the selected network's RSSI there; null where it wasn't heard.</summary>
    public IReadOnlyList<(double X, double Y, double? Rssi)> Marks { get; set; } = Array.Empty<(double, double, double?)>();

    /// <summary>Where a scan is being waited on, if anywhere.</summary>
    public (double X, double Y)? Pending { get; set; }

    public bool ShowHeatmap { get; set; } = true;

    public double Scale { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    // Heatmap cells in plan units, rebuilt when the marks change
    private List<(RectF Cell, Color Color)> _heat = new();
    private const int HeatCells = 72;     // along the plan's longer side
    private const double NotHeard = -100; // what a mark that didn't hear the network counts as

    public PointF ToScreen(double x, double y) => new((float)(x * Scale + OffsetX), (float)(y * Scale + OffsetY));
    public (double X, double Y) ToPlan(double sx, double sy) => ((sx - OffsetX) / Scale, (sy - OffsetY) / Scale);

    /// <summary>Scales and centres the plan to fit a view of the given size.</summary>
    public void Fit(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        Scale = Math.Min(width / PlanWidth, height / PlanHeight) * 0.95;
        OffsetX = (width - PlanWidth * Scale) / 2;
        OffsetY = (height - PlanHeight * Scale) / 2;
    }

    /// <summary>
    /// Rebuilds the heatmap from <see cref="Marks"/>: inverse-distance weighting between marks, out to about an
    /// eighth of the plan from the nearest one, so unsurveyed areas stay blank.
    /// </summary>
    public void RebuildHeatmap()
    {
        _heat = new List<(RectF, Color)>();
        if (Marks.Count == 0) return;
        double cell = Math.Max(PlanWidth, PlanHeight) / HeatCells;
        double reach = Math.Max(PlanWidth, PlanHeight) / 8;
        double reach2 = reach * reach;
        for (double y = 0; y < PlanHeight; y += cell)
        for (double x = 0; x < PlanWidth; x += cell)
        {
            double cx = x + cell / 2, cy = y + cell / 2, weights = 0, sum = 0, nearest = double.MaxValue;
            foreach (var m in Marks)
            {
                double d2 = (m.X - cx) * (m.X - cx) + (m.Y - cy) * (m.Y - cy);
                nearest = Math.Min(nearest, d2);
                if (d2 > reach2 * 4) continue;
                double w = 1 / Math.Max(d2, cell * cell / 4);
                weights += w;
                sum += w * (m.Rssi ?? NotHeard);
            }
            if (weights == 0 || nearest > reach2) continue;
            // Fade out towards the edge of the surveyed area
            float alpha = (float)(0.55 * (1 - Math.Sqrt(nearest) / reach * 0.6));
            _heat.Add((new RectF((float)x, (float)y, (float)cell, (float)cell), RssiColor(sum / weights).WithAlpha(alpha)));
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#F5F5F5");
        canvas.FillRectangle(dirtyRect);

        var origin = ToScreen(0, 0);
        float w = (float)(PlanWidth * Scale), h = (float)(PlanHeight * Scale);

        if (PlanImage is not null)
        {
            canvas.DrawImage(PlanImage, origin.X, origin.Y, w, h);
        }
        else
        {
            canvas.FillColor = Colors.White;
            canvas.FillRectangle(origin.X, origin.Y, w, h);
            canvas.StrokeColor = Color.FromArgb("#E0E0E0");
            canvas.StrokeSize = 1;
            for (int i = 1; i < 20; i++)
            {
                float gx = origin.X + w * i / 20, gy = origin.Y + h * i / 20;
                canvas.DrawLine(gx, origin.Y, gx, origin.Y + h);
                canvas.DrawLine(origin.X, gy, origin.X + w, gy);
            }
        }
        canvas.StrokeColor = Color.FromArgb("#9E9E9E");
        canvas.StrokeSize = 1;
        canvas.DrawRectangle(origin.X, origin.Y, w, h);

        if (ShowHeatmap)
        {
            float s = (float)Scale;
            foreach (var (cell, color) in _heat)
            {
                canvas.FillColor = color;
                // A hair larger than the cell, so no seams show between cells
                canvas.FillRectangle(origin.X + cell.X * s, origin.Y + cell.Y * s, cell.Width * s + 1, cell.Height * s + 1);
            }
        }

        foreach (var m in Marks)
        {
            var p = ToScreen(m.X, m.Y);
            canvas.FillColor = m.Rssi is { } r ? RssiColor(r) : Color.FromArgb("#464646");
            canvas.FillCircle(p.X, p.Y, 8);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2;
            canvas.DrawCircle(p.X, p.Y, 8);
            if (m.Rssi is null)
            {
                canvas.StrokeColor = Colors.White;
                canvas.DrawLine(p.X - 4, p.Y - 4, p.X + 4, p.Y + 4);
                canvas.DrawLine(p.X - 4, p.Y + 4, p.X + 4, p.Y - 4);
            }
        }

        if (Pending is { } pending)
        {
            var p = ToScreen(pending.X, pending.Y);
            canvas.StrokeColor = Color.FromArgb("#1565C0");
            canvas.StrokeSize = 3;
            canvas.DrawCircle(p.X, p.Y, 12);
            canvas.DrawLine(p.X - 18, p.Y, p.X - 6, p.Y);
            canvas.DrawLine(p.X + 6, p.Y, p.X + 18, p.Y);
            canvas.DrawLine(p.X, p.Y - 18, p.X, p.Y - 6);
            canvas.DrawLine(p.X, p.Y + 6, p.X, p.Y + 18);
        }
    }

    // WifiDB's signal-map colours (CreateApSigLayer), interpolated linearly between the stops
    private static readonly (double Rssi, Color Color)[] Ramp =
    {
        (-120, Color.FromArgb("#464646")), (-100, Color.FromArgb("#E42F00")), (-88, Color.FromArgb("#FF0000")),
        (-74, Color.FromArgb("#FF9200")), (-64, Color.FromArgb("#FFEC00")), (-52, Color.FromArgb("#80FF00")),
        (-40, Color.FromArgb("#0D7600")),
    };

    public static Color RssiColor(double rssi)
    {
        if (rssi <= Ramp[0].Rssi) return Ramp[0].Color;
        for (int i = 1; i < Ramp.Length; i++)
        {
            if (rssi > Ramp[i].Rssi) continue;
            var (r0, c0) = Ramp[i - 1];
            var (r1, c1) = Ramp[i];
            float t = (float)((rssi - r0) / (r1 - r0));
            return new Color(c0.Red + (c1.Red - c0.Red) * t, c0.Green + (c1.Green - c0.Green) * t, c0.Blue + (c1.Blue - c0.Blue) * t);
        }
        return Ramp[^1].Color;
    }
}
