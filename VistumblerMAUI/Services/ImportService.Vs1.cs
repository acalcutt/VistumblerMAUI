using System.Globalization;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

/// <summary>
/// The VS1 reader, a port of the original Vistumbler's _ImportVS1: GPS lines (12 fields, or 6 in old files) first,
/// then AP lines (15 fields in v4, 13 in v3) whose GID,SIGNAL,RSSI history points at those GPS lines, so each AP
/// comes in with its readings, positions and first/last seen times. Version 4.1 adds 10-field lines for cell towers
/// and Bluetooth devices, which older readers skip as a field count they don't know.
/// </summary>
public partial class ImportService
{
    private sealed record Vs1Gps(double? Lat, double? Lon, double? Alt, DateTime Time);

    private List<AccessPoint> ReadVs1(string[] lines)
    {
        // Pass 1: the GPS lines, by their id in the file
        var gps = new Dictionary<int, Vs1Gps>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if ((p.Length == 12 || p.Length == 6) && int.TryParse(p[0], out var gid) && ParseVs1Gps(p) is { } g)
                gps[gid] = g;
        }

        // Pass 2: APs, cell towers and Bluetooth devices
        var aps = new List<AccessPoint>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // VistumblerMAUI 0.8.0 wrote cells and Bluetooth as "#RADIO|Type|…|Manufacturer|GID,RSSI" comment lines,
            // without the High RSSI field; read them as the 4.1 lines that replaced them
            if (line.StartsWith("#RADIO|", StringComparison.Ordinal))
            {
                var old = line.Split('|');
                if (old.Length == 10) ParseVs1RadioLine([.. old[1..9], "", old[9]], gps);
                continue;
            }
            if (line[0] == '#') continue;
            var p = line.Split('|');
            AccessPoint? ap = p.Length switch
            {
                15 when IsMacAddress(p[1].Trim()) => ParseVs1ApLine(p, gps, v4: true),
                13 when IsMacAddress(p[1].Trim()) => ParseVs1ApLine(p, gps, v4: false),
                10 when ParseVs1RadioLine(p, gps) => null,   // 4.1: a cell tower or Bluetooth device, kept apart
                >= 19 when p[0] == "AP" => ParseVs1Line(p),   // this app's own early format
                _ => null,
            };
            if (ap is not null) aps.Add(ap);
        }
        return aps;
    }

    private static Vs1Gps? ParseVs1Gps(string[] p)
    {
        double? lat = DmmToDecimal(p[1]), lon = DmmToDecimal(p[2]);
        if (lat is null || lon is null || (lat == 0 && lon == 0)) { lat = null; lon = null; }
        string date = p.Length == 12 ? p[10] : p[4];
        string time = p.Length == 12 ? p[11] : p[5];
        double? alt = p.Length == 12 && double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null;
        return ParseVs1Time(date, time) is { } when ? new Vs1Gps(lat, lon, alt, when) : null;
    }

    // "2024-05-01" or the old "05-01-2024", with "12:34:56.789" or "12:34:56", in UTC
    private static DateTime? ParseVs1Time(string date, string time)
    {
        var d = date.Trim().Split('-');
        if (d.Length != 3) return null;
        string iso = d[0].Length == 4 ? date.Trim() : $"{d[2]}-{d[0].PadLeft(2, '0')}-{d[1].PadLeft(2, '0')}";
        return DateTime.TryParse($"{iso} {time.Trim()}", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }

    // "N 4807.0380" (degrees and minutes, the original's _Format_GPS_DMM) → signed decimal degrees
    private static double? DmmToDecimal(string text)
    {
        text = text.Trim();
        if (text.Length < 2) return null;
        char hemi = char.ToUpperInvariant(text[0]);
        if (hemi is not ('N' or 'S' or 'E' or 'W')) return null;
        if (!double.TryParse(text[1..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dmm)) return null;
        double deg = Math.Floor(dmm / 100);
        double value = deg + (dmm - deg * 100) / 60;
        return hemi is 'S' or 'W' ? -value : value;
    }

    // v4: SSID|BSSID|MANUF|Auth|Encr|SecType|Radio|Chan|BasicRates|OtherRates|HighSig|HighRSSI|NetType|Label|GID,SIG,RSSI\…
    // v3: SSID|BSSID|MANUF|Auth|Encr|SecType|Radio|Chan|BasicRates|OtherRates|NetType|Label|GID,SIG-GID,SIG…
    private AccessPoint? ParseVs1ApLine(string[] p, Dictionary<int, Vs1Gps> gps, bool v4)
    {
        var ap = new AccessPoint
        {
            Ssid = p[0].Trim(),
            Bssid = p[1].Trim(),
            Manufacturer = p[2].Trim(),
            Authentication = ParseAuthentication(p[3].Trim()),
            Encryption = ParseEncryption(p[4].Trim()),
            RadioType = p[6].Trim(),
            Channel = ParseInt(p[7].Trim()),
            BasicTransferRates = p[8].Trim(),
            OtherTransferRates = p[9].Trim(),
        };
        string netType = (v4 ? p[12] : p[10]).Trim();
        ap.NetworkType = netType.Contains("Ad", StringComparison.OrdinalIgnoreCase) ? NetworkType.Adhoc
            : Enum.TryParse<NetworkType>(netType, true, out var nt) ? nt : NetworkType.Infrastructure;
        string label = (v4 ? p[13] : p[11]).Trim();
        ap.Label = label == "Unknown" ? string.Empty : label;

        // The readings, each placed and timed by its GPS line
        foreach (var entry in (v4 ? p[14] : p[12]).Trim().Split(v4 ? '\\' : '-', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = entry.Split(',');
            if (f.Length < 2 || !int.TryParse(f[0], out var gid) || !gps.TryGetValue(gid, out var g)) continue;
            int signal = ParseInt(f[1].Replace("%", "").Trim());
            int rssi = f.Length >= 3 && int.TryParse(f[2], out var r) ? r : signal / 2 - 100;
            ap.SignalHistory.Add(new SignalHistory { Signal = signal, Rssi = rssi, Latitude = g.Lat, Longitude = g.Lon, Timestamp = g.Time });
        }

        var history = ap.SignalHistory;
        if (history.Count > 0)
        {
            ap.FirstSeen = history.Min(h => h.Timestamp);
            ap.LastSeen = history.Max(h => h.Timestamp);
            var last = history.MaxBy(h => h.Timestamp)!;
            ap.Signal = last.Signal;
            ap.Rssi = last.Rssi;
            if (history.Where(h => h.Latitude.HasValue).MaxBy(h => (h.Signal, h.Rssi ?? int.MinValue)) is { } best)
            {
                ap.Latitude = best.Latitude;
                ap.Longitude = best.Longitude;
            }
        }
        else
        {
            ap.FirstSeen = ap.LastSeen = DateTime.UtcNow;
        }
        ap.HighestSignal = v4 ? ParseInt(p[10].Trim()) : history.Select(h => h.Signal).DefaultIfEmpty(0).Max();
        ap.HighestRssi = v4 ? ParseInt(p[11].Trim()) : history.Select(h => h.Rssi ?? -100).DefaultIfEmpty(-100).Max();
        ap.Signal ??= ap.HighestSignal;
        ap.Rssi ??= ap.HighestRssi;
        return ap;
    }

    // 4.1: Type|Key|Name|Capabilities|Channel|Frequency|MfgrId|Manufacturer|High RSSI|GID,RSSI\GID,RSSI…
    // Adds the network to _radios with its readings; false when the line isn't one.
    private bool ParseVs1RadioLine(string[] p, Dictionary<int, Vs1Gps> gps)
    {
        // The type is a name ("LTE", "BLE"); Enum.TryParse would also take a number
        if (p.Length != 10 || int.TryParse(p[0], out _) || !Enum.TryParse<RadioNetworkType>(p[0].Trim(), true, out var type)) return false;
        string key = p[1].Trim();
        if (key.Length == 0) return false;
        if (!_radios.TryGetValue(key, out var n))
        {
            _radios[key] = n = new RadioNetwork
            {
                Key = key,
                Type = type,
                Name = p[2],
                Capabilities = p[3],
                Channel = int.TryParse(p[4], out var ch) ? ch : null,
                Frequency = ParseInt(p[5]),
                MfgrId = int.TryParse(p[6], out var m) ? m : null,
                Manufacturer = p[7],
            };
        }
        foreach (var entry in p[9].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = entry.Split(',');
            if (f.Length < 2 || !int.TryParse(f[0], out var gid) || !gps.TryGetValue(gid, out var g) || !int.TryParse(f[1], out var rssi))
                continue;
            n.History.Add(new RadioReading { Rssi = rssi, Latitude = g.Lat, Longitude = g.Lon, Altitude = g.Alt, Timestamp = g.Time });
            if (n.FirstSeen == default || g.Time < n.FirstSeen) n.FirstSeen = g.Time;
            if (g.Time >= n.LastSeen) { n.LastSeen = g.Time; n.Rssi = rssi; }
            if (rssi >= n.HighestRssi && g.Lat is not null) { n.HighestRssi = rssi; n.Latitude = g.Lat; n.Longitude = g.Lon; }
        }
        // Readings without a GPS fix aren't written, so a network heard only without one still has its strongest RSSI
        if (n.HighestRssi == int.MinValue && int.TryParse(p[8], out var high)) n.HighestRssi = n.Rssi = high;
        return true;
    }
}
