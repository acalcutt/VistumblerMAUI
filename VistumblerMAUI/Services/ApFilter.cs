using System.Text.Json;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

public enum FilterStatus { All, ActiveOnly, DeadOnly }

/// <summary>
/// A saved AP filter, after the original Vistumbler's Filters: it narrows the AP list, and the Export page can
/// export only the APs it lets through. The simple fields suit a phone; the advanced ones take the original's
/// per-field expressions (<see cref="FilterExpression"/>). An AP has to pass all of them.
/// </summary>
public sealed class ApFilter
{
    public string Id   { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New filter";

    // ── Simple ───────────────────────────────────────────────────────────────
    public string SsidContains   { get; set; } = string.Empty;
    public string BssidStartsWith { get; set; } = string.Empty;
    public bool Open   { get; set; } = true;
    public bool Wep    { get; set; } = true;
    public bool Secure { get; set; } = true;
    public bool Band24 { get; set; } = true;
    public bool Band5  { get; set; } = true;
    public bool Band6  { get; set; } = true;
    /// <summary>Channels to keep, e.g. "1,6,11"; empty keeps all.</summary>
    public string Channels { get; set; } = string.Empty;
    public int MinSignal { get; set; }
    public FilterStatus Status { get; set; } = FilterStatus.All;

    // ── Advanced: the original's fields and syntax ("*" = any) ───────────────
    public string AdvSsid       { get; set; } = "*";
    public string AdvBssid      { get; set; } = "*";
    public string AdvChannel    { get; set; } = "*";
    public string AdvAuth       { get; set; } = "*";
    public string AdvEncr       { get; set; } = "*";
    public string AdvRadio      { get; set; } = "*";
    public string AdvNetType    { get; set; } = "*";
    public string AdvSignal     { get; set; } = "*";
    public string AdvHighSignal { get; set; } = "*";
    public string AdvRssi       { get; set; } = "*";
    public string AdvHighRssi   { get; set; } = "*";
    public string AdvActive     { get; set; } = "*";

    public bool HasAdvanced => new[]
    {
        AdvSsid, AdvBssid, AdvChannel, AdvAuth, AdvEncr, AdvRadio, AdvNetType,
        AdvSignal, AdvHighSignal, AdvRssi, AdvHighRssi, AdvActive,
    }.Any(e => !FilterExpression.IsAny(e));

    /// <summary>Open (1), WEP (2) or secured (3), as the original's SecType: WEP networks report Open authentication.</summary>
    public static int SecurityType(AccessPoint ap) =>
        ap.Encryption == EncryptionType.WEP ? 2
        : ap.Authentication == AuthenticationType.Open && ap.Encryption == EncryptionType.None ? 1
        : 3;

    private static int BandOf(AccessPoint ap)
    {
        int f = ap.FrequencyMhz;
        if (f >= 5925) return 6;
        if (f >= 4900) return 5;
        if (f > 0) return 24;
        // Imports without a frequency: tell the band by channel (2.4 GHz uses 1-14)
        return ap.Channel is >= 1 and <= 14 ? 24 : ap.RadioType.Contains("6 GHz") ? 6 : 5;
    }

    public bool Matches(AccessPoint ap)
    {
        if (SsidContains.Length > 0 && !ap.Ssid.Contains(SsidContains, StringComparison.OrdinalIgnoreCase)) return false;
        if (BssidStartsWith.Length > 0 && !ap.Bssid.StartsWith(BssidStartsWith, StringComparison.OrdinalIgnoreCase)) return false;

        bool securityOk = SecurityType(ap) switch { 1 => Open, 2 => Wep, _ => Secure };
        if (!securityOk) return false;
        bool bandOk = BandOf(ap) switch { 24 => Band24, 5 => Band5, _ => Band6 };
        if (!bandOk) return false;

        if (Channels.Trim().Length > 0 && !FilterExpression.Matches(Channels, ap.Channel.ToString(), numeric: true)) return false;
        if (MinSignal > 0 && (ap.Signal ?? 0) < MinSignal) return false;
        if (Status == FilterStatus.ActiveOnly && !ap.IsActive) return false;
        if (Status == FilterStatus.DeadOnly && ap.IsActive) return false;

        return FilterExpression.Matches(AdvSsid, ap.Ssid, false)
            && FilterExpression.Matches(AdvBssid, ap.Bssid, false)
            && FilterExpression.Matches(AdvChannel, ap.Channel.ToString(), true)
            && FilterExpression.Matches(AdvAuth, ap.AuthText, false)
            && FilterExpression.Matches(AdvEncr, ap.EncryptionText, false)
            && FilterExpression.Matches(AdvRadio, ap.RadioType, false)
            && FilterExpression.Matches(AdvNetType, ap.NetworkType == NetworkType.Adhoc ? "Ad Hoc" : "Infrastructure", false)
            && FilterExpression.Matches(AdvSignal, (ap.Signal ?? 0).ToString(), true)
            && FilterExpression.Matches(AdvHighSignal, (ap.HighestSignal ?? 0).ToString(), true)
            && FilterExpression.Matches(AdvRssi, ap.Rssi?.ToString() ?? "", true)
            && FilterExpression.Matches(AdvHighRssi, ap.HighestRssi?.ToString() ?? "", true)
            && FilterExpression.Matches(AdvActive, ap.IsActive ? "1" : "0", true);
    }

    /// <summary>What the filter does, in a line, for the filter list.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (SsidContains.Length > 0) parts.Add($"SSID contains \"{SsidContains}\"");
            if (BssidStartsWith.Length > 0) parts.Add($"BSSID {BssidStartsWith}…");
            if (!(Open && Wep && Secure))
                parts.Add(string.Join("/", new[] { Open ? "open" : null, Wep ? "WEP" : null, Secure ? "secure" : null }.Where(s => s is not null)));
            if (!(Band24 && Band5 && Band6))
                parts.Add(string.Join("/", new[] { Band24 ? "2.4" : null, Band5 ? "5" : null, Band6 ? "6" : null }.Where(s => s is not null)) + " GHz");
            if (Channels.Trim().Length > 0) parts.Add($"channels {Channels.Trim()}");
            if (MinSignal > 0) parts.Add($"signal ≥ {MinSignal}%");
            if (Status != FilterStatus.All) parts.Add(Status == FilterStatus.ActiveOnly ? "active only" : "dead only");
            if (HasAdvanced) parts.Add("advanced rules");
            return parts.Count == 0 ? "Shows every AP" : string.Join(" · ", parts);
        }
    }

    public ApFilter Clone() => JsonSerializer.Deserialize<ApFilter>(JsonSerializer.Serialize(this))!;
}

/// <summary>The saved filters and which one is in use, backed by MAUI <see cref="Preferences"/>.</summary>
public static class ApFilterStore
{
    private const string FiltersKey = "Filters_List";
    private const string ActiveKey  = "Filters_Active";

    private static List<ApFilter>? _filters;

    /// <summary>Raised when the filters or the one in use change, so the AP list can be redrawn.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<ApFilter> All => Filters;

    private static List<ApFilter> Filters
    {
        get
        {
            if (_filters is not null) return _filters;
            try { _filters = JsonSerializer.Deserialize<List<ApFilter>>(Preferences.Get(FiltersKey, "[]")) ?? new(); }
            catch { _filters = new(); }
            return _filters;
        }
    }

    /// <summary>The filter in use, or null when every AP is shown.</summary>
    public static ApFilter? Active
    {
        get
        {
            var id = Preferences.Get(ActiveKey, string.Empty);
            return id.Length == 0 ? null : Filters.FirstOrDefault(f => f.Id == id);
        }
    }

    public static void SetActive(ApFilter? filter)
    {
        Preferences.Set(ActiveKey, filter?.Id ?? string.Empty);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Adds the filter, or replaces the saved one with the same id.</summary>
    public static void Save(ApFilter filter)
    {
        int i = Filters.FindIndex(f => f.Id == filter.Id);
        if (i >= 0) Filters[i] = filter; else Filters.Add(filter);
        Persist();
    }

    public static void Delete(ApFilter filter)
    {
        Filters.RemoveAll(f => f.Id == filter.Id);
        if (Preferences.Get(ActiveKey, string.Empty) == filter.Id) Preferences.Set(ActiveKey, string.Empty);
        Persist();
    }

    private static void Persist()
    {
        Preferences.Set(FiltersKey, JsonSerializer.Serialize(Filters));
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
