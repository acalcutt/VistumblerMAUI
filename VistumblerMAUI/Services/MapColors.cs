using System.Globalization;

namespace VistumblerMAUI.Services;

/// <summary>
/// Per-bucket access-point circle colors for the map, persisted via MAUI
/// <see cref="Preferences"/>. Each "bucket" is either a live-scan pseudo-bucket
/// (live_active / live_dead) or a WifiDB history age bucket. Every bucket has an
/// Open / WEP / Secure color. Defaults form a brightness gradient — the live scan's
/// active APs are brightest and each older tier is progressively darker — so recent
/// history is easy to tell apart from the current scan. Mirrors VistumblerCS's Map
/// settings tab. Cell buckets are not user-configurable (single graduated purple).
/// </summary>
public static class MapColors
{
    // Base (brightest) colors — used for the live scan's active APs. 6-hex, no '#'.
    public const string BaseOpen   = "1AFF66";
    public const string BaseWep    = "FFAD33";
    public const string BaseSecure = "FF1A1A";

    /// <summary>(renderer key, display name, brightness factor 0..1 applied to the base colors).</summary>
    public static readonly IReadOnlyList<(string Key, string Name, double Factor)> Buckets = new[]
    {
        ("live_active", "Live (Active)", 1.00),
        ("live_dead",   "Live (Dead)",   0.78),
        ("daily",       "Daily",         0.65),
        ("weekly",      "Weekly",        0.60),
        ("monthly",     "Monthly",       0.55),
        ("0to1year",    "0 – 1 year",    0.50),
        ("1to2year",    "1 – 2 years",   0.45),
        ("2to3year",    "2 – 3 years",   0.40),
        ("3to5year",    "3 – 5 years",   0.35),
        ("5to10year",   "5 – 10 years",  0.28),
        ("10yrplus",    "10+ years",     0.20),
    };

    /// <summary>Bumped whenever any color changes, so the map can detect and reapply.</summary>
    public static int Revision { get; private set; }

    private static double FactorFor(string key)
    {
        foreach (var b in Buckets) if (b.Key == key) return b.Factor;
        return 1.0;
    }

    /// <summary>Default 6-hex (no '#') for a bucket/sectype: the base color scaled by the bucket factor.</summary>
    public static string Default(string key, string sectype)
    {
        var baseHex = sectype switch { "Open" => BaseOpen, "Wep" => BaseWep, _ => BaseSecure };
        return ScaleHex(baseHex, FactorFor(key));
    }

    /// <summary>Current 6-hex (no '#') for a bucket/sectype (persisted value or computed default).</summary>
    public static string Get(string key, string sectype)
        => Normalize(Preferences.Get(PrefKey(key, sectype), Default(key, sectype)));

    /// <summary>Persist a color (hex with or without '#') for a bucket/sectype and bump the revision.</summary>
    public static void Set(string key, string sectype, string hex)
    {
        var norm = Normalize(hex);
        if (!IsValid(norm) || norm == Get(key, sectype)) return;
        Preferences.Set(PrefKey(key, sectype), norm);
        Revision++;
    }

    /// <summary>All three colors for a bucket, each "#RRGGBB", ready to hand to maplibre paint.</summary>
    public static (string Open, string Wep, string Secure) Hashed(string key)
        => ("#" + Get(key, "Open"), "#" + Get(key, "Wep"), "#" + Get(key, "Secure"));

    public static bool IsValid(string? hex)
    {
        var h = Normalize(hex);
        return h.Length == 6 && int.TryParse(h, NumberStyles.HexNumber, null, out _);
    }

    private static string PrefKey(string key, string sectype) => $"MapColor_{key}_{sectype}";

    // Strip a leading '#' or '0x'/'0X' prefix only — never legitimate leading zeros
    // of the color itself (e.g. "0A6629" must stay "0A6629").
    private static string Normalize(string? hex)
    {
        hex = (hex ?? string.Empty).Trim();
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex[2..];
        return hex.ToUpperInvariant();
    }

    private static string ScaleHex(string hex, double factor)
    {
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, null, out int rgb))
            return hex;
        int Scale(int c) => Math.Clamp((int)Math.Round(c * factor), 0, 255);
        int r = Scale((rgb >> 16) & 0xFF), g = Scale((rgb >> 8) & 0xFF), b = Scale(rgb & 0xFF);
        return $"{r:X2}{g:X2}{b:X2}";
    }
}
