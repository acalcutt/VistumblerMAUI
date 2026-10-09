namespace VistumblerMAUI.Controls;

/// <summary>
/// A compass card with a needle pointing along the GPS track angle (direction of travel), like the original
/// Vistumbler's GPS Compass window. Used by the GPS details page's GraphicsView.
/// </summary>
public class CompassDrawable : IDrawable
{
    /// <summary>Direction of travel in degrees clockwise from north, or null when unknown (e.g. standing still).</summary>
    public double? Heading { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        float cx = dirtyRect.Center.X, cy = dirtyRect.Center.Y;
        float r = size / 2 - 4;

        // Card
        canvas.StrokeColor = Color.FromArgb("#BDBDBD");
        canvas.StrokeSize  = 2;
        canvas.DrawCircle(cx, cy, r);

        // Ticks every 30°, longer at the cardinal points
        for (int deg = 0; deg < 360; deg += 30)
        {
            double a = deg * Math.PI / 180;
            float inner = r * (deg % 90 == 0 ? 0.82f : 0.9f);
            canvas.DrawLine(cx + (float)Math.Sin(a) * inner, cy - (float)Math.Cos(a) * inner,
                            cx + (float)Math.Sin(a) * r,     cy - (float)Math.Cos(a) * r);
        }

        canvas.FontSize = Math.Max(10, size / 14);
        foreach (var (label, deg) in new[] { ("N", 0), ("E", 90), ("S", 180), ("W", 270) })
        {
            double a = deg * Math.PI / 180;
            float lx = cx + (float)Math.Sin(a) * r * 0.68f, ly = cy - (float)Math.Cos(a) * r * 0.68f;
            canvas.FontColor = label == "N" ? Color.FromArgb("#C62828") : Color.FromArgb("#616161");
            canvas.DrawString(label, lx - 15, ly - 10, 30, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        if (Heading is not { } heading)
        {
            canvas.FontColor = Color.FromArgb("#9E9E9E");
            canvas.FontSize  = Math.Max(10, size / 18);
            canvas.DrawString("No heading", cx - 60, cy - 10, 120, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        // Needle: red half points along the heading, grey half opposite
        double h = heading * Math.PI / 180;
        float tipX = cx + (float)Math.Sin(h) * r * 0.55f, tipY = cy - (float)Math.Cos(h) * r * 0.55f;
        float tailX = cx - (float)Math.Sin(h) * r * 0.4f, tailY = cy + (float)Math.Cos(h) * r * 0.4f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeSize  = Math.Max(4, size / 40);
        canvas.StrokeColor = Color.FromArgb("#9E9E9E");
        canvas.DrawLine(cx, cy, tailX, tailY);
        canvas.StrokeColor = Color.FromArgb("#C62828");
        canvas.DrawLine(cx, cy, tipX, tipY);
        canvas.FillColor = Color.FromArgb("#424242");
        canvas.FillCircle(cx, cy, Math.Max(4, size / 40));
    }
}
