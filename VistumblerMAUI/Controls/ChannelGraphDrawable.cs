namespace VistumblerMAUI.Controls;

public enum GraphBand { TwoPointFourGHz, FiveGHz, SixGHz }

/// <summary>One AP rendered on the channel graph (center freq + half channel width + signal).</summary>
public record ChannelEntry(string Ssid, int FreqMhz, int HalfWidthMhz, int Signal, int Rssi, Color Fill, Color Stroke);

/// <summary>
/// MAUI GraphicsView drawable that plots APs as filled bell curves by center frequency —
/// the channel graph from VistumblerCS ported to <see cref="ICanvas"/>. 2.4 GHz highlights the
/// non-overlapping channels 1/6/11; 5 and 6 GHz show their channel markers.
/// </summary>
public class ChannelGraphDrawable : IDrawable
{
    public GraphBand Band { get; set; } = GraphBand.TwoPointFourGHz;
    public bool UseRssi { get; set; }

    private IReadOnlyList<ChannelEntry> _entries = Array.Empty<ChannelEntry>();
    public void SetEntries(IReadOnlyList<ChannelEntry> entries) => _entries = entries ?? Array.Empty<ChannelEntry>();

    private const float LeftBorder = 40, TopBorder = 6, RightBorder = 8, BottomBorder = 22;

    private record BandDef(int FreqMin, int FreqMax, (int Freq, string Label)[] Markers, int[] HighlightFreqs);

    private static readonly BandDef Def24 = new(2400, 2496,
        new[] { (2412,"1"),(2417,"2"),(2422,"3"),(2427,"4"),(2432,"5"),(2437,"6"),(2442,"7"),
                (2447,"8"),(2452,"9"),(2457,"10"),(2462,"11"),(2467,"12"),(2472,"13"),(2484,"14") },
        new[] { 2412, 2437, 2462 });

    private static readonly BandDef Def5 = new(5150, 5990,
        new[] { (5180,"36"),(5200,"40"),(5220,"44"),(5240,"48"),(5260,"52"),(5280,"56"),(5300,"60"),(5320,"64"),
                (5500,"100"),(5520,"104"),(5540,"108"),(5560,"112"),(5580,"116"),(5600,"120"),(5620,"124"),(5640,"128"),
                (5660,"132"),(5680,"136"),(5700,"140"),(5720,"144"),(5745,"149"),(5765,"153"),(5785,"157"),(5805,"161"),
                (5825,"165"),(5845,"169"),(5865,"173"),(5885,"177") },
        Array.Empty<int>());

    private static readonly BandDef Def6 = new(5925, 7130,
        new[] { (5955,"1"),(5995,"9"),(6035,"17"),(6075,"25"),(6115,"33"),(6155,"41"),(6195,"49"),(6235,"57"),
                (6275,"65"),(6315,"73"),(6355,"81"),(6395,"89"),(6435,"97"),(6475,"105"),(6515,"113"),(6555,"121"),
                (6595,"129"),(6635,"137"),(6675,"145"),(6715,"153"),(6755,"161"),(6795,"169"),(6835,"177"),(6875,"185"),
                (6915,"193"),(6955,"201"),(6995,"209"),(7035,"217"),(7075,"225"),(7115,"233") },
        Array.Empty<int>());

    private static BandDef BandFor(GraphBand b) => b switch
    {
        GraphBand.FiveGHz => Def5,
        GraphBand.SixGHz  => Def6,
        _                 => Def24
    };

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0) return;

        float plotX = LeftBorder, plotY = TopBorder;
        float plotW = w - LeftBorder - RightBorder;
        float plotH = h - TopBorder - BottomBorder;
        if (plotW < 10 || plotH < 10) return;

        var def = BandFor(Band);

        canvas.FillColor = Color.FromRgb(0xCC, 0xCC, 0xAA);
        canvas.FillRectangle(0, 0, w, h);
        canvas.FillColor = Color.FromRgb(0xF8, 0xF8, 0xF0);
        canvas.FillRectangle(plotX, plotY, plotW, plotH);

        // 2.4 GHz non-overlapping channel highlights (1/6/11)
        if (Band == GraphBand.TwoPointFourGHz)
        {
            canvas.FillColor = Color.FromRgba(0, 180, 0, 30);
            foreach (int cf in def.HighlightFreqs)
            {
                float x1 = Math.Max(FreqToX(cf - 11, plotX, plotW, def), plotX);
                float x2 = Math.Min(FreqToX(cf + 11, plotX, plotW, def), plotX + plotW);
                if (x2 > x1) canvas.FillRectangle(x1, plotY, x2 - x1, plotH);
            }
        }

        // Horizontal grid
        canvas.StrokeSize = 1;
        for (int i = 0; i <= 10; i++)
        {
            float y = plotY + plotH * (i / 10f);
            canvas.StrokeColor = i % 2 == 0 ? Color.FromRgba(0xAA, 0xAA, 0x77, 150) : Color.FromRgba(0xAA, 0xAA, 0x77, 60);
            canvas.DrawLine(plotX, y, plotX + plotW, y);
        }

        // Channel marker lines
        canvas.StrokeColor = Color.FromRgba(0x77, 0x77, 0x44, 80);
        foreach (var (freq, _) in def.Markers)
        {
            float x = FreqToX(freq, plotX, plotW, def);
            if (x >= plotX && x <= plotX + plotW) canvas.DrawLine(x, plotY, x, plotY + plotH);
        }

        // Bell curves — weakest first so the strongest sits on top
        foreach (var e in _entries.OrderBy(e => UseRssi ? e.Rssi : e.Signal))
            DrawBell(canvas, e, def, plotX, plotY, plotW, plotH);

        // SSID labels on top
        canvas.FontSize = 10;
        foreach (var e in _entries.OrderBy(e => UseRssi ? e.Rssi : e.Signal))
            DrawLabel(canvas, e, def, plotX, plotY, plotW, plotH);

        // Y axis labels
        canvas.FontColor = Colors.Black;
        canvas.FontSize = 9;
        for (int i = 0; i <= 10; i += 2)
        {
            float y = plotY + plotH * (i / 10f);
            string label = UseRssi ? (i * -10).ToString() : ((10 - i) * 10) + "%";
            canvas.DrawString(label, 2, y - 7, LeftBorder - 6, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // X axis channel labels (thinned to avoid overlap)
        float minSpacing = Band == GraphBand.TwoPointFourGHz ? 24 : 30;
        float prevX = float.NegativeInfinity;
        foreach (var (freq, label) in def.Markers)
        {
            float x = FreqToX(freq, plotX, plotW, def);
            if (x < plotX || x > plotX + plotW || x - prevX < minSpacing) continue;
            canvas.DrawString(label, x - 14, plotY + plotH + 2, 28, 16, HorizontalAlignment.Center, VerticalAlignment.Center);
            prevX = x;
        }

        // Border
        canvas.StrokeColor = Color.FromRgb(0x66, 0x66, 0x44);
        canvas.DrawRectangle(plotX, plotY, plotW, plotH);
    }

    private void DrawBell(ICanvas canvas, ChannelEntry e, BandDef def, float plotX, float plotY, float plotW, float plotH)
    {
        float xc = FreqToX(e.FreqMhz, plotX, plotW, def);
        float xl = FreqToX(e.FreqMhz - e.HalfWidthMhz, plotX, plotW, def);
        float xr = FreqToX(e.FreqMhz + e.HalfWidthMhz, plotX, plotW, def);
        float yTop = SignalToY(e, plotY, plotH);
        float yBot = plotY + plotH;

        const float minHalf = 3;
        if (xc - xl < minHalf) { xl = xc - minHalf; xr = xc + minHalf; }

        float halfW = xc - xl;
        float height = yBot - yTop;
        if (height < 1) return;

        float cy1 = yTop + height * 0.25f;
        float cx1L = xl + halfW * 0.35f;
        float cx1R = xr - halfW * 0.35f;

        var path = new PathF();
        path.MoveTo(xl, yBot);
        path.CurveTo(xl, cy1, cx1L, yTop, xc, yTop);     // bottom-left → peak
        path.CurveTo(cx1R, yTop, xr, cy1, xr, yBot);     // peak → bottom-right
        path.Close();

        canvas.FillColor = e.Fill;
        canvas.FillPath(path);
        canvas.StrokeColor = e.Stroke;
        canvas.StrokeSize = 1.5f;
        canvas.DrawPath(path);
    }

    private void DrawLabel(ICanvas canvas, ChannelEntry e, BandDef def, float plotX, float plotY, float plotW, float plotH)
    {
        float xc = FreqToX(e.FreqMhz, plotX, plotW, def);
        float yTop = SignalToY(e, plotY, plotH);
        if (xc < plotX - 20 || xc > plotX + plotW + 20) return;

        string text = string.IsNullOrEmpty(e.Ssid) ? "(hidden)" : e.Ssid;
        const float boxW = 92, boxH = 14;
        float bx = xc - boxW / 2;
        float by = yTop - boxH - 2;
        if (by < plotY) by = yTop + 2;

        canvas.FillColor = Color.FromRgba(255, 255, 255, 180);
        canvas.FillRoundedRectangle(bx, by, boxW, boxH, 2);
        canvas.FontColor = Color.FromRgb(0x2F, 0x3A, 0x6E);
        canvas.DrawString(text, bx, by, boxW, boxH, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private float SignalToY(ChannelEntry e, float plotY, float plotH)
    {
        float frac = UseRssi ? Math.Clamp((e.Rssi + 100f) / 100f, 0, 1) : Math.Clamp(e.Signal / 100f, 0, 1);
        return plotY + plotH * (1f - frac);
    }

    private static float FreqToX(int freqMhz, float plotX, float plotW, BandDef def)
        => plotX + plotW * (freqMhz - def.FreqMin) / (float)(def.FreqMax - def.FreqMin);
}
