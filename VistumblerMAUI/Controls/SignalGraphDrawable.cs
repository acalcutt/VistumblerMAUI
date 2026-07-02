namespace VistumblerMAUI.Controls;

/// <summary>
/// Draws a signal-strength-over-time line graph for one access point, from its recorded
/// SignalHistory percentages (0–100). Used by the AP details page's GraphicsView — the
/// MAUI counterpart of the signal graph in VistumblerCS / VistumblerMDB.
/// </summary>
public class SignalGraphDrawable : IDrawable
{
    private IReadOnlyList<int> _points = Array.Empty<int>();

    /// <summary>Replace the plotted series (signal percentages, oldest → newest).</summary>
    public void SetPoints(IReadOnlyList<int> points) => _points = points ?? Array.Empty<int>();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float w = dirtyRect.Width, h = dirtyRect.Height;
        const float pad = 6f;

        // Gridlines at 0 / 25 / 50 / 75 / 100 %.
        canvas.StrokeColor = Color.FromArgb("#EEEEEE");
        canvas.StrokeSize  = 1;
        canvas.FontColor   = Color.FromArgb("#9E9E9E");
        canvas.FontSize    = 9;
        for (int pct = 0; pct <= 100; pct += 25)
        {
            float y = pad + (h - 2 * pad) * (1 - pct / 100f);
            canvas.DrawLine(pad, y, w - pad, y);
            canvas.DrawString($"{pct}", 0, y - 6, pad + 12, 12, HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        if (_points.Count == 0)
        {
            canvas.FontColor = Color.FromArgb("#9E9E9E");
            canvas.FontSize  = 12;
            canvas.DrawString("No signal history yet", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        float X(int i) => _points.Count == 1
            ? w - pad
            : pad + (w - 2 * pad) * i / (_points.Count - 1);
        float Y(int pct) => pad + (h - 2 * pad) * (1 - Math.Clamp(pct, 0, 100) / 100f);

        // Single sample: just a dot.
        if (_points.Count == 1)
        {
            canvas.FillColor = Color.FromArgb("#1565C0");
            canvas.FillCircle(X(0), Y(_points[0]), 3);
            return;
        }

        var path = new PathF();
        for (int i = 0; i < _points.Count; i++)
        {
            float x = X(i), y = Y(_points[i]);
            if (i == 0) path.MoveTo(x, y);
            else        path.LineTo(x, y);
        }

        canvas.StrokeColor = Color.FromArgb("#1565C0");
        canvas.StrokeSize  = 2;
        canvas.DrawPath(path);
    }
}
