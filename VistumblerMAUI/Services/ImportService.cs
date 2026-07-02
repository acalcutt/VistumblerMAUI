using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;
using SQLite;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Imports access point data from Vistumbler (VS1/VSZ), NetStumbler (NS1), Kismet
/// (NetXML / KismetDB) and CSV (Vistumbler-detailed / WiGLE) formats.
/// Ported from VistumblerCS's ImportService — adapted to use sqlite-net-pcl for the
/// KismetDB reader (so we don't pull in a second SQLite provider alongside the one
/// already used for the app's own database) and MAUI's flat SignalHistory.Latitude/
/// Longitude fields (no nested GpsData object).
/// </summary>
public class ImportService : IImportService
{
    public async Task<List<AccessPoint>> ImportFromNs1Async(string filePath)
    {
        return await Task.Run(() =>
        {
            var accessPoints = new List<AccessPoint>();
            if (!File.Exists(filePath)) return accessPoints;

            using var fs = File.OpenRead(filePath);
            using var reader = new BinaryReader(fs);

            // Read Header
            if (fs.Length < 12) return accessPoints;
            var signature = new string(reader.ReadChars(4));
            if (signature != "NetS") return accessPoints; // Invalid signature

            var version = reader.ReadUInt32();
            if (version != 12) return accessPoints; // Only version 12 supported

            var apCount = reader.ReadUInt32();

            for (int i = 0; i < apCount; i++)
            {
                var ap = new AccessPoint();

                // Read AP Info
                var ssidLength = reader.ReadByte();
                var ssidBytes = reader.ReadBytes(ssidLength);
                ap.Ssid = System.Text.Encoding.ASCII.GetString(ssidBytes);

                var bssidBytes = reader.ReadBytes(6);
                ap.Bssid = BitConverter.ToString(bssidBytes).Replace("-", ":");

                // NS1 stores signal/noise in dBm. Keep dBm as RSSI and derive the percentage.
                var maxSignalDbm = reader.ReadInt32(); // MaxSignal (dBm)
                ap.HighestRssi   = maxSignalDbm;
                ap.HighestSignal = DbToPercent(maxSignalDbm);

                var minNoise = reader.ReadInt32();
                var maxSnr   = reader.ReadInt32(); // MaxSNR (not RSSI) — informational only

                var flags = reader.ReadUInt32();
                // Map flags if needed. Bit 4 is Privacy.
                if ((flags & 0x0010) != 0)
                {
                    ap.Encryption = EncryptionType.WEP;
                }
                else
                {
                    ap.Encryption = EncryptionType.None;
                }

                // Network Type (ESS vs IBSS)
                if ((flags & 0x0001) != 0) ap.NetworkType = NetworkType.Infrastructure;
                else if ((flags & 0x0002) != 0) ap.NetworkType = NetworkType.Adhoc;

                var beaconInterval = reader.ReadUInt32();

                var firstSeenFileTime = reader.ReadInt64();
                try { ap.FirstSeen = DateTime.FromFileTimeUtc(firstSeenFileTime); } catch { ap.FirstSeen = DateTime.MinValue; }

                var lastSeenFileTime = reader.ReadInt64();
                try { ap.LastSeen = DateTime.FromFileTimeUtc(lastSeenFileTime); } catch { ap.LastSeen = DateTime.MinValue; }

                var bestLat = reader.ReadDouble();
                var bestLong = reader.ReadDouble();

                if (bestLat != 0 || bestLong != 0)
                {
                    ap.Latitude = bestLat;
                    ap.Longitude = bestLong;
                }

                var dataCount = reader.ReadUInt32();

                // Signal History
                int? lastDbm = null;
                for (int j = 0; j < dataCount; j++)
                {
                    var hist = new SignalHistory();

                    var histTime = reader.ReadInt64();
                    try { hist.Timestamp = DateTime.FromFileTimeUtc(histTime); } catch { hist.Timestamp = DateTime.MinValue; }

                    int dbm = reader.ReadInt32();      // Signal (dBm)
                    hist.Rssi   = dbm;
                    hist.Signal = DbToPercent(dbm);
                    lastDbm     = dbm;
                    var histNoise = reader.ReadInt32();

                    var locationSource = reader.ReadInt32();

                    if (locationSource == 1) // GPS
                    {
                        hist.Latitude = reader.ReadDouble();
                        hist.Longitude = reader.ReadDouble();
                        reader.ReadDouble(); // Altitude (not tracked on flat SignalHistory)
                        reader.ReadUInt32(); // NumberOfSatellites
                        reader.ReadDouble(); // SpeedKmh
                        reader.ReadDouble(); // TrackAngle
                        reader.ReadDouble(); // MagVar
                        reader.ReadDouble(); // HorizontalDilution
                    }

                    ap.SignalHistory.Add(hist);
                }

                // Current signal = the most recent sample (fall back to the AP max).
                int currentDbm = lastDbm ?? maxSignalDbm;
                ap.Rssi   = currentDbm;
                ap.Signal = DbToPercent(currentDbm);

                // Remaining AP fields
                var nameLength = reader.ReadByte();
                var nameBytes = reader.ReadBytes(nameLength);
                // ignoring name

                var channels = reader.ReadUInt64();
                var lastChannel = reader.ReadUInt32();
                ap.Channel = (int)lastChannel;

                var ipAddress = reader.ReadUInt32();
                var minSignal = reader.ReadInt32();
                var maxNoise = reader.ReadInt32();
                var dataRate = reader.ReadUInt32();

                var ipSubnet = reader.ReadUInt32();
                var ipMask = reader.ReadUInt32();
                var apFlags = reader.ReadUInt32();

                // Map Vistumbler Custom Flags for Auth/Encryption
                // 0x0001: WPA-Personal     0x0002: WPA-Enterprise
                // 0x0004: WPA2-Personal    0x0008: WPA2-Enterprise
                // 0x0010: WPA3             0x0020: OWE
                // 0x0040: TKIP             0x0080: CCMP/AES
                // 0x0100: GCMP             0x0200: GCMP_256
                // 0x0400: CCMP_256         0x0800: BIP

                // Auth
                if ((apFlags & 0x0020) != 0) ap.Authentication = AuthenticationType.OWE;
                else if ((apFlags & 0x0010) != 0) ap.Authentication = AuthenticationType.WPA3_PSK;
                else if ((apFlags & 0x0008) != 0) ap.Authentication = AuthenticationType.WPA2_Enterprise;
                else if ((apFlags & 0x0004) != 0) ap.Authentication = AuthenticationType.WPA2_PSK;
                else if ((apFlags & 0x0002) != 0) ap.Authentication = AuthenticationType.WPA_Enterprise;
                else if ((apFlags & 0x0001) != 0) ap.Authentication = AuthenticationType.WPA_PSK;

                // Encryption
                if ((apFlags & 0x0800) != 0) ap.Encryption = EncryptionType.BIP;
                else if ((apFlags & 0x0400) != 0) ap.Encryption = EncryptionType.CCMP_256;
                else if ((apFlags & 0x0200) != 0) ap.Encryption = EncryptionType.GCMP_256;
                else if ((apFlags & 0x0100) != 0) ap.Encryption = EncryptionType.GCMP;
                else if ((apFlags & 0x0080) != 0) ap.Encryption = EncryptionType.CCMP;
                else if ((apFlags & 0x0040) != 0) ap.Encryption = EncryptionType.TKIP;

                var ieLength = reader.ReadUInt32();
                reader.ReadBytes((int)ieLength); // Skip IEs

                accessPoints.Add(ap);
            }

            return accessPoints;
        });
    }

    public async Task<List<AccessPoint>> ImportFromNetXmlAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            var accessPoints = new List<AccessPoint>();
            if (!File.Exists(filePath)) return accessPoints;

            try
            {
                var doc = XDocument.Load(filePath);
                var networks = doc.Descendants("wireless-network");

                foreach (var net in networks)
                {
                    var ap = new AccessPoint();
                    ap.Bssid = net.Element("BSSID")?.Value ?? string.Empty;
                    var ssidElem = net.Element("SSID");
                    ap.Ssid = ssidElem?.Element("essid")?.Value ?? string.Empty;
                    ap.Manufacturer = net.Element("manuf")?.Value ?? string.Empty;
                    ap.Channel = ParseInt(net.Element("channel")?.Value ?? "0");

                    var firstTime = net.Attribute("first-time")?.Value;
                    var lastTime = net.Attribute("last-time")?.Value;
                    ap.FirstSeen = ParseKismetDate(firstTime) ?? DateTime.UtcNow;
                    ap.LastSeen = ParseKismetDate(lastTime) ?? DateTime.UtcNow;

                    var snr = net.Element("snr-info");
                    if (snr != null)
                    {
                        // NetXML signal values are dBm — keep them as RSSI, derive the percentage.
                        int lastDbm = ParseInt(snr.Element("last_signal_dbm")?.Value ?? "0");
                        int maxDbm  = ParseInt(snr.Element("max_signal_dbm")?.Value ?? "0");
                        if (maxDbm == 0) maxDbm = lastDbm;

                        ap.Rssi          = lastDbm;
                        ap.Signal        = lastDbm < 0 ? DbToPercent(lastDbm) : 0;
                        ap.HighestRssi   = maxDbm;
                        ap.HighestSignal = maxDbm < 0 ? DbToPercent(maxDbm) : 0;
                    }

                    var gps = net.Element("gps-info");
                    if (gps != null)
                    {
                        var latStr = gps.Element("peak-lat")?.Value ?? gps.Element("avg-lat")?.Value ?? "0";
                        var lonStr = gps.Element("peak-lon")?.Value ?? gps.Element("avg-lon")?.Value ?? "0";
                        var lat = ParseDouble(latStr);
                        var lon = ParseDouble(lonStr);

                        if (lat.HasValue && lon.HasValue && (lat.Value != 0 || lon.Value != 0))
                        {
                            ap.Latitude = lat;
                            ap.Longitude = lon;
                        }
                    }

                    // Encryption is the au3's "<Auth>-<Encr>" string (e.g. "WPA2-Personal-CCMP").
                    var encStr = ssidElem?.Element("encryption")?.Value ?? "";
                    if (!string.IsNullOrEmpty(encStr))
                    {
                        bool ent = encStr.Contains("Enterprise") || encStr.Contains("EAP");
                        if (encStr.Contains("WPA3"))
                            ap.Authentication = ent ? AuthenticationType.WPA3_Enterprise : AuthenticationType.WPA3_PSK;
                        else if (encStr.Contains("WPA2"))
                            ap.Authentication = ent ? AuthenticationType.WPA2_Enterprise : AuthenticationType.WPA2_PSK;
                        else if (encStr.Contains("WPA"))
                            ap.Authentication = ent ? AuthenticationType.WPA_Enterprise : AuthenticationType.WPA_PSK;
                        else if (encStr.Contains("OWE"))
                            ap.Authentication = AuthenticationType.OWE;
                        else
                            ap.Authentication = AuthenticationType.Open;

                        ap.Encryption = ParseEncryption(encStr);   // CCMP/GCMP/TKIP/WEP/None
                    }

                    var typeStr = net.Attribute("type")?.Value;
                    if (typeStr == "infrastructure") ap.NetworkType = NetworkType.Infrastructure;
                    else if (typeStr == "ad-hoc") ap.NetworkType = NetworkType.Adhoc;

                    accessPoints.Add(ap);
                }
            }
            catch
            {
                // Ignore errors
            }
            return accessPoints;
        });
    }

    /// <summary>Row shape for the ad-hoc sqlite-net-pcl query against a KismetDB file's `devices` table.</summary>
    private class KismetDeviceRow
    {
        public string? Devmac { get; set; }
        public string? Type { get; set; }
        public double? StrongestSignal { get; set; }
        public double? MinLat { get; set; }
        public double? MinLon { get; set; }
        public byte[]? DeviceBlob { get; set; }
        public string? DeviceText { get; set; }
    }

    public async Task<List<AccessPoint>> ImportFromKismetDbAsync(string filePath)
    {
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        try
        {
            var conn = new SQLiteAsyncConnection(filePath, SQLiteOpenFlags.ReadOnly);
            try
            {
                var tableExists = await conn.ExecuteScalarAsync<string>(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='devices'");
                if (string.IsNullOrEmpty(tableExists)) return accessPoints;

                // `device` can be stored as either TEXT or BLOB depending on the Kismet version that wrote it,
                // so pull it twice under different declared types and use whichever one came back non-null.
                var rows = await conn.QueryAsync<KismetDeviceRow>(@"
                    SELECT devmac AS Devmac, type AS Type, strongest_signal AS StrongestSignal,
                           min_lat AS MinLat, min_lon AS MinLon,
                           device AS DeviceBlob, device AS DeviceText
                    FROM devices
                    WHERE type IN ('Wi-Fi AP','Wi-Fi Ad-Hoc','Wi-Fi','infrastructure','ad-hoc')");

                foreach (var row in rows)
                {
                    var ap = new AccessPoint { Bssid = row.Devmac ?? "" };

                    // strongest_signal is dBm — keep as RSSI, derive the percentage.
                    int sigDbm = (int)(row.StrongestSignal ?? 0);
                    ap.HighestRssi   = sigDbm;
                    ap.Rssi          = sigDbm;
                    ap.HighestSignal = sigDbm < 0 ? DbToPercent(sigDbm) : 0;
                    ap.Signal        = ap.HighestSignal;

                    ap.Latitude = row.MinLat ?? 0;
                    ap.Longitude = row.MinLon ?? 0;

                    ap.NetworkType = (row.Type?.Contains("Ad-Hoc", StringComparison.OrdinalIgnoreCase) == true)
                        ? NetworkType.Adhoc : NetworkType.Infrastructure;

                    string? jsonStr = row.DeviceText;
                    if (string.IsNullOrWhiteSpace(jsonStr) && row.DeviceBlob is { Length: > 0 } bytes)
                    {
                        try { jsonStr = System.Text.Encoding.UTF8.GetString(bytes); } catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(jsonStr))
                    {
                        try { ParseKismetDeviceJson(jsonStr, ap); }
                        catch { }
                    }

                    accessPoints.Add(ap);
                }
            }
            finally
            {
                await conn.CloseAsync();
            }
        }
        catch
        {
            // Ignore errors — return whatever we managed to parse
        }
        return accessPoints;
    }

    private void ParseKismetDeviceJson(string jsonStr, AccessPoint ap)
    {
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        if (TryGetPropertyPath(root, "kismet.device.base.manuf", out var manuf))
            ap.Manufacturer = manuf.GetString() ?? string.Empty;

        // robust SSID extraction
        var ssidFound = false;

        if (root.TryGetProperty("kismet.device.base.name", out var kName) && kName.ValueKind == JsonValueKind.String)
        {
            var n = kName.GetString();
            if (!string.IsNullOrEmpty(n) && n != ap.Bssid) { ap.Ssid = n; ssidFound = true; }
        }

        var dot11Device = default(JsonElement);
        var hasDot11 = root.TryGetProperty("dot11.device", out dot11Device);

        if (!ssidFound && hasDot11)
        {
            if (dot11Device.TryGetProperty("dot11.device.last_beaconed_ssid", out var lbSsid) && lbSsid.ValueKind == JsonValueKind.String)
            {
                ap.Ssid = lbSsid.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(ap.Ssid)) ssidFound = true;
            }

            if (!ssidFound)
            {
                if (dot11Device.TryGetProperty("dot11.device.last_beaconed_ssid_record", out var lbRec))
                {
                    if (lbRec.TryGetProperty("dot11.advertisedssid.ssid", out var advSsid))
                    {
                        ap.Ssid = advSsid.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(ap.Ssid)) ssidFound = true;
                    }
                }
            }
        }

        if (!ssidFound)
        {
            if (TryGetPropertyPath(root, "kismet.device.base.commonname", out var cname) && cname.ValueKind == JsonValueKind.String)
            {
                var cn = cname.GetString();
                if (!string.IsNullOrEmpty(cn) && cn != ap.Bssid) ap.Ssid = cn;
            }
        }

        // Channel Extraction
        if (root.TryGetProperty("kismet.device.base.channel", out var chan))
        {
            if (int.TryParse(chan.ToString(), out var ch)) ap.Channel = ch;
        }
        else if (hasDot11)
        {
            if (dot11Device.TryGetProperty("dot11.device.last_beaconed_ssid_record", out var lbRec) &&
                lbRec.TryGetProperty("dot11.advertisedssid.channel", out var advChan))
            {
                if (int.TryParse(advChan.ToString(), out var ch)) ap.Channel = ch;
            }
        }

        // Auth/Encryption
        string cryptString = "";
        if (root.TryGetProperty("kismet.device.base.crypt_string", out var cs)) cryptString = cs.GetString() ?? "";
        else if (root.TryGetProperty("kismet.device.base.encryption", out var enc)) cryptString = enc.GetString() ?? "";

        if (string.IsNullOrEmpty(cryptString) && hasDot11)
        {
            if (dot11Device.TryGetProperty("dot11.device.last_beaconed_ssid_record", out var lbRec) &&
                lbRec.TryGetProperty("dot11.advertisedssid.crypt_string", out var recCrypt))
            {
                cryptString = recCrypt.GetString() ?? "";
            }
        }

        if (!string.IsNullOrEmpty(cryptString))
        {
            if (cryptString.Contains("/"))
            {
                // "<Auth>/<Encr>" (e.g. "WPA2-Personal/CCMP") as written by the au3 and our export.
                var parts = cryptString.Split('/');
                string a = parts[0];
                bool ent = a.Contains("Enterprise") || a.Contains("EAP");
                if (a.Contains("WPA3"))      ap.Authentication = ent ? AuthenticationType.WPA3_Enterprise : AuthenticationType.WPA3_PSK;
                else if (a.Contains("WPA2")) ap.Authentication = ent ? AuthenticationType.WPA2_Enterprise : AuthenticationType.WPA2_PSK;
                else if (a.Contains("WPA"))  ap.Authentication = ent ? AuthenticationType.WPA_Enterprise : AuthenticationType.WPA_PSK;
                else if (a.Contains("OWE"))  ap.Authentication = AuthenticationType.OWE;
                else                         ap.Authentication = AuthenticationType.Open;

                ap.Encryption = ParseEncryption(parts.Length > 1 ? parts[1] : cryptString);
            }
            else
            {
                ParseKismetCrypt(cryptString, out var parsedAuth, out var parsedEncr);
                ap.Authentication = parsedAuth;
                ap.Encryption = parsedEncr;
            }
        }

        // Vistumbler Custom Fields & Radio Type Fallback
        var isRadioTypeSet = false;
        if (TryGetPropertyPath(root, "vistumbler.device.radio_type", out var rt) && rt.ValueKind == JsonValueKind.String)
        {
            ap.RadioType = rt.GetString() ?? "";
            if (!string.IsNullOrEmpty(ap.RadioType) && ap.RadioType != "Unknown") isRadioTypeSet = true;
        }

        if (!isRadioTypeSet)
        {
            if (TryGetPropertyPath(root, "kismet.device.base.frequency", out var freqElem))
            {
                double freqKhz = 0;
                if (freqElem.ValueKind == JsonValueKind.Number) freqElem.TryGetDouble(out freqKhz);

                double freqMhz = freqKhz / 1000.0;

                if (freqMhz >= 5925) ap.RadioType = "802.11ax";
                else if (freqMhz >= 4900 && freqMhz <= 5900) ap.RadioType = "802.11ac";
                else if (freqMhz >= 2400 && freqMhz <= 2500) ap.RadioType = "802.11n";
            }
        }

        if (TryGetPropertyPath(root, "vistumbler.device.signal_quality", out var sq))
        {
            if (sq.ValueKind == JsonValueKind.Number && sq.TryGetInt32(out var sVal))
                ap.Signal = sVal;
        }
    }

    private void ParseKismetCrypt(string cryptString, out AuthenticationType auth, out EncryptionType encr)
    {
        auth = AuthenticationType.Open;
        encr = EncryptionType.None;

        var crypt = (cryptString ?? "").ToUpperInvariant();

        if (crypt.Contains("WPA3"))
        {
            if (crypt.Contains("SAE") || crypt.Contains("PERSONAL"))
            {
                auth = AuthenticationType.WPA3_PSK;
            }
            else if (crypt.Contains("ENTERPRISE") || crypt.Contains("1X"))
            {
                auth = AuthenticationType.WPA2_Enterprise;
            }
            else
            {
                auth = AuthenticationType.WPA3_PSK;
            }
        }
        else if (crypt.Contains("WPA2"))
        {
            if (crypt.Contains("PSK") || crypt.Contains("PERSONAL"))
            {
                auth = AuthenticationType.WPA2_PSK;
            }
            else if (crypt.Contains("ENTERPRISE") || crypt.Contains("1X"))
            {
                auth = AuthenticationType.WPA2_Enterprise;
            }
            else
            {
                auth = AuthenticationType.WPA2_PSK;
            }
        }
        else if (crypt.Contains("WPA"))
        {
            if (crypt.Contains("PSK") || crypt.Contains("PERSONAL"))
            {
                auth = AuthenticationType.WPA_PSK;
            }
            else if (crypt.Contains("ENTERPRISE") || crypt.Contains("1X"))
            {
                auth = AuthenticationType.WPA_Enterprise;
            }
            else
            {
                auth = AuthenticationType.WPA_PSK;
            }
        }
        else if (crypt.Contains("WEP"))
        {
            auth = AuthenticationType.Open;
            encr = EncryptionType.WEP;
        }
        else if (crypt.Contains("OPEN") || string.IsNullOrEmpty(crypt))
        {
            auth = AuthenticationType.Open;
            encr = EncryptionType.None;
        }

        if (encr == EncryptionType.None && !crypt.Contains("WEP"))
        {
            if (crypt.Contains("CCMP") || crypt.Contains("AES"))
            {
                encr = EncryptionType.CCMP;
            }
            else if (crypt.Contains("TKIP"))
            {
                encr = EncryptionType.TKIP;
            }
            else if (crypt.Contains("GCMP"))
            {
                encr = EncryptionType.GCMP;
            }
            else if (crypt.Contains("WPA"))
            {
                encr = EncryptionType.CCMP;
            }
        }
    }

    private bool TryGetPropertyPath(JsonElement element, string path, out JsonElement result)
    {
        // Strategy 1: Attempt to find the full path as a single key (Kismet 'flatter' style)
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(path, out result))
        {
            return true;
        }

        // Strategy 2: Traverse by splitting dots (Nested style)
        var parts = path.Split('.');
        var current = element;
        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            else
            {
                result = default;
                return false;
            }
        }
        result = current;
        return true;
    }

    private DateTime? ParseKismetDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;

        // Try Kismet Legacy: "Fri Feb 09 10:00:00 2024"
        if (DateTime.TryParseExact(dateStr, "ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var kdt)) return kdt;

        return null;
    }

    public async Task<List<AccessPoint>> ImportFromVs1Async(string filePath)
    {
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        var lines = await File.ReadAllLinesAsync(filePath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split('|');
            // Check for valid VS1 line (starts with AP and has enough parts)
            if (parts.Length >= 19 && parts[0] == "AP")
            {
                var ap = ParseVs1Line(parts);
                if (ap != null) accessPoints.Add(ap);
            }
            // Check for Vistumbler V4 VS1 format (at least 15 columns, index 1 is BSSID)
            else if (parts.Length >= 15 && IsMacAddress(parts[1]))
            {
                var ap = ParseExternalVs1Line(parts);
                if (ap != null) accessPoints.Add(ap);
            }
        }
        return accessPoints;
    }

    private bool IsMacAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Regex.IsMatch(value, @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$");
    }

    private AccessPoint? ParseExternalVs1Line(string[] parts)
    {
        try
        {
            // V4 Format:
            // 0:SSID|1:BSSID|2:MANUF|3:Auth|4:Encr|5:SecType|6:RadType|7:Chan
            // 8:BasicRates|9:OtherRates|10:HighSignal|11:HighRSSI|12:NetType|13:Label|14:History

            var ap = new AccessPoint
            {
                Ssid = parts[0],
                Bssid = parts[1],
                Manufacturer = parts[2],
                Authentication = ParseAuthentication(parts[3]),
                Encryption = ParseEncryption(parts[4]),
                RadioType = parts[6],
                Channel = ParseInt(parts[7]),
                BasicTransferRates = parts[8],
                OtherTransferRates = parts[9],
                HighestSignal = ParseInt(parts[10]),
                HighestRssi = ParseInt(parts[11]),
                NetworkType = Enum.TryParse<NetworkType>(parts[12], true, out var nt) ? nt : NetworkType.Unknown,
                Label = parts[13] == "Unknown" ? string.Empty : parts[13],
                FirstSeen = DateTime.Now, // Default since dates are in GPS section
                LastSeen = DateTime.Now
            };

            // Use Highest as current if history parsing is skipped for now
            ap.Signal = ap.HighestSignal;
            ap.Rssi = ap.HighestRssi;

            // History format: GID,SIGNAL,RSSI\GID,SIGNAL,RSSI
            if (parts.Length > 14 && !string.IsNullOrWhiteSpace(parts[14]))
            {
                var entries = parts[14].Split('\\');
                if (entries.Length > 0)
                {
                    var lastEntry = entries[entries.Length - 1].Split(',');
                    if (lastEntry.Length >= 3)
                    {
                        ap.Signal = ParseInt(lastEntry[1]);
                        ap.Rssi = ParseInt(lastEntry[2]);
                    }
                }
            }

            return ap;
        }
        catch
        {
            return null;
        }
    }

    private AuthenticationType ParseAuthentication(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AuthenticationType.Unknown;

        if (value.Equals("WPA2-Personal", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_PSK;
        if (value.Equals("WPA-Personal", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_PSK;
        if (value.Equals("WPA2-Enterprise", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_Enterprise;
        if (value.Equals("WPA-Enterprise", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_Enterprise;

        if (value.Contains("WPA3", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Contains("192", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA3_Enterprise_192;
                return AuthenticationType.WPA3_Enterprise;
            }
            if (value.Contains("SAE", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Personal", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("PSK", StringComparison.OrdinalIgnoreCase))
                return AuthenticationType.WPA3_PSK;

            return AuthenticationType.WPA3; // Generic
        }

        if (value.Equals("OWE", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.OWE;
        if (value.Equals("Open", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.Open;
        if (value.Equals("WEP", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.Open; // Auth is Open, Enc is WEP usually

        return Enum.TryParse<AuthenticationType>(value, true, out var auth) ? auth : AuthenticationType.Unknown;
    }

    private EncryptionType ParseEncryption(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return EncryptionType.Unknown;

        if (value.Equals("AES", StringComparison.OrdinalIgnoreCase)) return EncryptionType.AES;
        if (value.Equals("CCMP", StringComparison.OrdinalIgnoreCase)) return EncryptionType.CCMP;
        if (value.Equals("None", StringComparison.OrdinalIgnoreCase)) return EncryptionType.None;

        if (value.Contains("GCMP-256", StringComparison.OrdinalIgnoreCase)) return EncryptionType.GCMP_256;
        if (value.Contains("GCMP", StringComparison.OrdinalIgnoreCase)) return EncryptionType.GCMP;
        if (value.Contains("CCMP-256", StringComparison.OrdinalIgnoreCase)) return EncryptionType.CCMP_256;
        if (value.Contains("BIP", StringComparison.OrdinalIgnoreCase)) return EncryptionType.BIP;

        return Enum.TryParse<EncryptionType>(value, true, out var enc) ? enc : EncryptionType.Unknown;
    }

    public async Task<List<AccessPoint>> ImportFromVszAsync(string filePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "VistumblerImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(filePath, tempDir);

            var vs1File = Directory.GetFiles(tempDir, "*.vs1").FirstOrDefault()
                          ?? Directory.GetFiles(tempDir, "*.txt").FirstOrDefault();

            if (vs1File != null)
            {
                return await ImportFromVs1Async(vs1File);
            }
            return new List<AccessPoint>();
        }
        catch
        {
            return new List<AccessPoint>();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task<List<AccessPoint>> ImportFromCsvAsync(string filePath)
    {
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length == 0) return accessPoints;

        var header = lines[0];
        bool isWigle = header.Contains("WigleWifi", StringComparison.OrdinalIgnoreCase) ||
                       header.Contains("MAC,SSID,AuthMode", StringComparison.OrdinalIgnoreCase);

        if (isWigle)
        {
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var ap = ParseWigleLine(SplitCsvLine(lines[i]));
                if (ap != null) accessPoints.Add(ap);
            }
            return accessPoints;
        }

        // Vistumbler Detailed CSV — one row per observation; group by BSSID and accumulate
        // signal history (matches the _ExportToCSV Detailed=1 column layout).
        var byBssid = new Dictionary<string, AccessPoint>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var p = SplitCsvLine(lines[i]);
            if (p.Length < 17) continue;   // need at least SSID..LONGITUDE

            var bssid = p[1].Trim();
            if (string.IsNullOrEmpty(bssid)) continue;

            if (!byBssid.TryGetValue(bssid, out var ap))
            {
                ap = new AccessPoint
                {
                    Bssid          = bssid,
                    Ssid           = p[0],
                    Manufacturer   = p[2],
                    HighestSignal  = ParseInt(p[4]),
                    HighestRssi    = ParseInt(p[6]),
                    Authentication = ParseAuthentication(p[7]),
                    Encryption     = ParseEncryption(p[8]),
                    RadioType      = p[9],
                    Channel        = ParseInt(p[10]),
                    BasicTransferRates = p[11],
                    OtherTransferRates = p[12],
                    NetworkType    = p[13].Contains("Ad", StringComparison.OrdinalIgnoreCase)
                                     ? NetworkType.Adhoc : NetworkType.Infrastructure,
                    Label          = p[14],
                    FirstSeen      = DateTime.MaxValue,
                    LastSeen       = DateTime.MinValue,
                };
                byBssid[bssid] = ap;
                accessPoints.Add(ap);
            }

            var hist = new SignalHistory
            {
                Signal    = ParseInt(p[3]),
                Rssi      = ParseInt(p[5]),
                Latitude  = ParseDouble(p[15]),
                Longitude = ParseDouble(p[16]),
                Timestamp = DateTime.UtcNow,
            };
            if (p.Length > 25)
            {
                var dt = $"{p[24]} {p[25]}".Trim();
                if (DateTime.TryParse(dt, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
                    hist.Timestamp = t;
            }
            ap.SignalHistory.Add(hist);

            // Latest sample = current; track first/last seen.
            ap.Signal = hist.Signal;
            ap.Rssi   = hist.Rssi;
            if (hist.Latitude.HasValue) { ap.Latitude = hist.Latitude; ap.Longitude = hist.Longitude; }
            if (hist.Timestamp < ap.FirstSeen) ap.FirstSeen = hist.Timestamp;
            if (hist.Timestamp > ap.LastSeen)  ap.LastSeen  = hist.Timestamp;
        }

        foreach (var ap in accessPoints)
        {
            if (ap.FirstSeen == DateTime.MaxValue) ap.FirstSeen = DateTime.UtcNow;
            if (ap.LastSeen  == DateTime.MinValue) ap.LastSeen  = ap.FirstSeen;
        }

        return accessPoints;
    }

    // --- Helpers ---

    private AccessPoint? ParseVs1Line(string[] parts)
    {
        try
        {
            // Expected indices based on ExportToVs1Async:
            // 0: AP, 1: Id, 2: Bssid, 3: Ssid, 4: Chan, 5: Auth, 6: Encr, 7: RadType, 8: BasicRates, 9: OtherRates, 10: NetType
            // 11: Sig, 12: HighSig, 13: Rssi, 14: HighRssi, 15: Lat, 16: Lon, 17: FirstSeen, 18: LastSeen, 19: Manuf, 20: Label

            var ap = new AccessPoint
            {
                Bssid = parts[2],
                Ssid = parts[3],
                Channel = ParseInt(parts[4]),
                Authentication = Enum.TryParse<AuthenticationType>(parts[5], true, out var auth) ? auth : AuthenticationType.Unknown,
                Encryption = Enum.TryParse<EncryptionType>(parts[6], true, out var enc) ? enc : EncryptionType.Unknown,
                RadioType = parts[7],
                NetworkType = Enum.TryParse<NetworkType>(parts[10], true, out var nt) ? nt : NetworkType.Unknown,
                Signal = ParseInt(parts[11]),
                HighestSignal = ParseInt(parts[12]),
                Rssi = ParseInt(parts[13]),
                HighestRssi = ParseInt(parts[14]),
                Latitude = ParseDouble(parts[15]),
                Longitude = ParseDouble(parts[16]),
                FirstSeen = DateTime.TryParse(parts[17], out var fs) ? fs : DateTime.UtcNow,
                LastSeen = DateTime.TryParse(parts[18], out var ls) ? ls : DateTime.UtcNow,
            };

            if (parts.Length > 19) ap.Manufacturer = parts[19];
            if (parts.Length > 20) ap.Label = parts[20];

            return ap;
        }
        catch
        {
            return null; // Skip malformed lines
        }
    }

    private AccessPoint? ParseWigleLine(string[] parts)
    {
        if (parts.Length < 7) return null;
        try
        {
            // v1.4: MAC,SSID,AuthMode,FirstSeen,Channel,RSSI,Lat,Lon,Alt,Accuracy,Type  (11 cols)
            // v1.6: MAC,SSID,AuthMode,FirstSeen,Channel,Frequency,RSSI,Lat,Lon,Alt,Accuracy,Type (12 cols)
            bool isV16 = parts.Length >= 12;
            int rssiIdx = isV16 ? 6 : 5;
            int latIdx = isV16 ? 7 : 6;
            int lonIdx = isV16 ? 8 : 7;

            var ap = new AccessPoint
            {
                Bssid = parts[0],
                Ssid = parts[1],
                Channel = ParseInt(parts[4]),
                Rssi = ParseInt(parts[rssiIdx]),
                Latitude = parts.Length > latIdx ? ParseDouble(parts[latIdx]) : null,
                Longitude = parts.Length > lonIdx ? ParseDouble(parts[lonIdx]) : null,
                FirstSeen = DateTime.TryParse(parts[3], out var fs) ? fs : DateTime.UtcNow,
                LastSeen = DateTime.TryParse(parts[3], out var ls) ? ls : DateTime.UtcNow,
            };

            if (isV16 && int.TryParse(parts[5], out var freq) && freq > 0)
                ap.Channel = GetChannelFromFreq(freq);

            ParseWigleAuthMode(parts[2], out var auth, out var encr, out var netType);
            ap.Authentication = auth;
            ap.Encryption = encr;
            ap.NetworkType = netType;

            // Derive Signal % from RSSI (RSSI range roughly -100..0 -> 0..100 %)
            if (ap.Rssi.HasValue)
                ap.Signal = Math.Clamp((ap.Rssi.Value + 100) * 2, 0, 100);

            return ap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Port of _WigleCSV_ParseAuthMode: parses WiGLE capability flags to Vistumbler auth/encr/netType.
    /// </summary>
    private static void ParseWigleAuthMode(string authMode, out AuthenticationType auth, out EncryptionType encr, out NetworkType netType)
    {
        auth = AuthenticationType.Open;
        encr = EncryptionType.None;
        netType = NetworkType.Infrastructure;

        var cap = authMode.ToUpperInvariant();

        if (cap.Contains("GCMP-256")) encr = EncryptionType.GCMP_256;
        else if (cap.Contains("GCMP")) encr = EncryptionType.GCMP;
        else if (cap.Contains("CCMP-256")) encr = EncryptionType.CCMP_256;
        else if (cap.Contains("CCMP") || cap.Contains("AES")) encr = EncryptionType.AES;
        else if (cap.Contains("TKIP")) encr = EncryptionType.TKIP;
        else if (cap.Contains("WEP")) encr = EncryptionType.WEP;

        if (cap.Contains("WPA3") || cap.Contains("SAE") || cap.Contains("EAP_SUITE_B_192"))
        {
            auth = cap.Contains("EAP") ? AuthenticationType.WPA3_Enterprise : AuthenticationType.WPA3_PSK;
        }
        else if (cap.Contains("WPA2") || cap.Contains("RSN"))
        {
            auth = cap.Contains("EAP") ? AuthenticationType.WPA2_Enterprise : AuthenticationType.WPA2_PSK;
        }
        else if (cap.Contains("WPA"))
        {
            auth = cap.Contains("EAP") ? AuthenticationType.WPA_Enterprise : AuthenticationType.WPA_PSK;
        }
        else if (cap.Contains("OWE"))
        {
            auth = AuthenticationType.OWE;
        }
        else if (encr == EncryptionType.WEP)
        {
            auth = AuthenticationType.Open;
        }
        else
        {
            auth = AuthenticationType.Open;
            encr = EncryptionType.None;
        }

        if (cap.Contains("IBSS") || cap.Contains("AD-HOC"))
            netType = NetworkType.Adhoc;
    }

    private static int GetChannelFromFreq(int freqMhz)
    {
        if (freqMhz == 2484) return 14;
        if (freqMhz >= 2412 && freqMhz <= 2477) return (freqMhz - 2407) / 5;
        if (freqMhz >= 5180 && freqMhz <= 5885) return (freqMhz - 5000) / 5;
        return 0;
    }

    private int ParseInt(string s) => int.TryParse(s, out var i) ? i : 0;

    // dBm → signal percentage (inverse of the export's "%/2 - 100"): 0% at -100 dBm, 100% at -50.
    private static int DbToPercent(int dbm) => Math.Clamp((dbm + 100) * 2, 0, 100);
    private double? ParseDouble(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private string[] SplitCsvLine(string line)
    {
        // Quick valid-enough CSV split: splits by comma, but not commas inside quotes
        var result = new List<string>();
        var current = "";
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(UnescapeCsv(current));
                current = "";
            }
            else
            {
                current += c;
            }
        }
        result.Add(UnescapeCsv(current));
        return result.ToArray();
    }

    private string UnescapeCsv(string field)
    {
        field = field.Trim();
        if (field.StartsWith("\"") && field.EndsWith("\""))
        {
            field = field.Substring(1, field.Length - 2);
            field = field.Replace("\"\"", "\"");
        }
        return field;
    }
}
