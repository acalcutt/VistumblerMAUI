using System.Text;
using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Xml;
using SQLite;
using Vistumbler.Core.Extensions;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Exports access point data to KML, GPX, NS1, KismetDB, NetXML, CSV, WiGLE CSV,
/// VS1 and VSZ. Ported from VistumblerCS's ExportService — adapted to write KismetDB
/// output via sqlite-net-pcl (so we don't pull in a second SQLite provider) and to
/// use MAUI's flat SignalHistory.Latitude/Longitude fields (no nested GpsData object).
/// </summary>
public class ExportService : IExportService
{
    public async Task ExportToNs1Async(string filePath, List<AccessPoint> accessPoints)
    {
        await Task.Run(() =>
        {
            using var fs = File.Create(filePath);
            using var writer = new BinaryWriter(fs);

            // Header
            writer.Write("NetS".ToCharArray());
            writer.Write((uint)12);
            writer.Write((uint)accessPoints.Count);

            foreach (var ap in accessPoints)
            {
                // SSID
                var ssidBytes = Encoding.ASCII.GetBytes(ap.Ssid ?? "");
                writer.Write((byte)ssidBytes.Length);
                writer.Write(ssidBytes);

                // BSSID
                var bssidParts = (ap.Bssid ?? "00:00:00:00:00:00").Split(':', '-');
                if (bssidParts.Length == 6)
                {
                    foreach (var part in bssidParts)
                    {
                        try { writer.Write(Convert.ToByte(part, 16)); } catch { writer.Write((byte)0); }
                    }
                }
                else
                {
                    writer.Write(new byte[6]);
                }

                writer.Write((int)(ap.HighestSignal ?? 0));
                writer.Write((int)0); // MinNoise (unknown)
                writer.Write((int)(ap.HighestRssi ?? 0));

                // Flags
                uint flags = 0;
                if (ap.NetworkType == NetworkType.Infrastructure) flags |= 0x0001;
                else if (ap.NetworkType == NetworkType.Adhoc) flags |= 0x0002;

                if (ap.Encryption != EncryptionType.None)
                {
                    flags |= 0x0010;
                }
                writer.Write(flags);

                writer.Write((uint)100); // Beacon Interval (dummy)

                writer.Write(ToFileTimeSafe(ap.FirstSeen));
                writer.Write(ToFileTimeSafe(ap.LastSeen));

                writer.Write(ap.Latitude ?? 0.0);
                writer.Write(ap.Longitude ?? 0.0);

                // Signal History
                writer.Write((uint)ap.SignalHistory.Count);
                foreach (var hist in ap.SignalHistory)
                {
                    writer.Write(ToFileTimeSafe(hist.Timestamp));
                    writer.Write(hist.Signal);
                    writer.Write((int)0); // Noise

                    if (hist.Latitude.HasValue && hist.Longitude.HasValue)
                    {
                        writer.Write((int)1); // LocationSource = GPS
                        writer.Write(hist.Latitude.Value);
                        writer.Write(hist.Longitude.Value);
                        writer.Write((double)0); // Altitude (not tracked on flat SignalHistory)
                        writer.Write((uint)0);   // NumberOfSatellites
                        writer.Write((double)0); // Speed (KMH)
                        writer.Write((double)0); // TrackAngle
                        writer.Write((double)0); // MagVar
                        writer.Write((double)0); // HorizontalDilution
                    }
                    else
                    {
                        writer.Write((int)0); // LocationSource = None
                    }
                }

                // Name (using Label or SSID)
                var name = ap.Label;
                if (string.IsNullOrEmpty(name)) name = ap.Ssid;
                var nameBytes = Encoding.ASCII.GetBytes(name ?? "");
                writer.Write((byte)nameBytes.Length);
                writer.Write(nameBytes);

                // Channels (Bitmask)
                ulong channelsMask = GetChannelBitMask(ap.Channel);
                writer.Write(channelsMask);

                writer.Write((uint)ap.Channel); // LastChannel

                writer.Write((uint)0); // IP
                writer.Write((int)(ap.Signal ?? 0)); // MinSignal
                writer.Write((int)0); // MaxNoise
                writer.Write((uint)0); // DataRate
                writer.Write((uint)0); // IPSubnet
                writer.Write((uint)0); // IPMask

                // Calculate ApFlags for Vistumbler Custom Fields
                uint apFlags = 0;

                if (ap.Authentication == AuthenticationType.WPA_PSK) apFlags |= 0x0001;
                else if (ap.Authentication == AuthenticationType.WPA_Enterprise) apFlags |= 0x0002;
                else if (ap.Authentication == AuthenticationType.WPA2_PSK) apFlags |= 0x0004;
                else if (ap.Authentication == AuthenticationType.WPA2_Enterprise) apFlags |= 0x0008;

                if (ap.Authentication == AuthenticationType.WPA3_PSK ||
                    ap.Authentication.ToString().Contains("WPA3"))
                {
                    apFlags |= 0x0010;
                }

                if (ap.Authentication == AuthenticationType.OWE) apFlags |= 0x0020;

                if (ap.Encryption == EncryptionType.TKIP) apFlags |= 0x0040;
                else if (ap.Encryption == EncryptionType.CCMP || ap.Encryption == EncryptionType.AES) apFlags |= 0x0080;

                if (ap.Encryption == EncryptionType.GCMP) apFlags |= 0x0100;
                if (ap.Encryption == EncryptionType.GCMP_256) apFlags |= 0x0200;
                if (ap.Encryption == EncryptionType.CCMP_256) apFlags |= 0x0400;
                if (ap.Encryption.ToString().StartsWith("BIP")) apFlags |= 0x0800;

                writer.Write(apFlags); // ApFlags

                writer.Write((uint)0); // IELength — no IEs
            }
        });
    }

    public async Task ExportToKismetDbAsync(string filePath, List<AccessPoint> accessPoints)
    {
        if (File.Exists(filePath)) File.Delete(filePath);

        var conn = new SQLiteAsyncConnection(filePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
        try
        {
            var schemaStatements = new[]
            {
                "CREATE TABLE IF NOT EXISTS KISMET (kismet_version TEXT, db_version INTEGER, db_module TEXT)",
                "INSERT OR REPLACE INTO KISMET (kismet_version, db_version, db_module) VALUES ('2023-07', 10, 'kismetlog')",
                "CREATE TABLE IF NOT EXISTS alerts (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, devmac TEXT, lat REAL, lon REAL, header TEXT, json BLOB)",
                "CREATE TABLE IF NOT EXISTS data (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, devmac TEXT, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, datasource TEXT, type TEXT, json BLOB, signal INTEGER)",
                "CREATE TABLE IF NOT EXISTS datasources (uuid TEXT, typestring TEXT, definition TEXT, name TEXT, interface TEXT, json BLOB, UNIQUE(uuid) ON CONFLICT REPLACE)",
                "INSERT OR REPLACE INTO datasources (uuid, typestring, definition, name, interface, json) VALUES ('00000000-0000-0000-0000-000000000000', 'vistumbler', 'vistumbler', 'vistumbler', 'vistumbler', '{}')",
                "CREATE TABLE IF NOT EXISTS devices (first_time INTEGER, last_time INTEGER, devkey TEXT, phyname TEXT, devmac TEXT, strongest_signal INTEGER, min_lat REAL, min_lon REAL, max_lat REAL, max_lon REAL, avg_lat REAL, avg_lon REAL, bytes_data INTEGER, type TEXT, device BLOB, UNIQUE(phyname, devmac) ON CONFLICT REPLACE)",
                "CREATE INDEX IF NOT EXISTS idx_devices_devkey ON devices(devkey)",
                "CREATE INDEX IF NOT EXISTS idx_devices_devmac ON devices(devmac)",
                "CREATE TABLE IF NOT EXISTS messages (ts_sec INTEGER, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, msgtype TEXT, message TEXT)",
                "CREATE TABLE IF NOT EXISTS packets (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, sourcemac TEXT, destmac TEXT, transmac TEXT, frequency REAL, devkey TEXT, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, packet_len INTEGER, signal INTEGER, datasource TEXT, dlt INTEGER, packet BLOB, error INTEGER, tags TEXT, datarate REAL, hash INTEGER, packetid INTEGER, packet_full_len INTEGER)",
                "CREATE INDEX IF NOT EXISTS idx_packets_sourcemac ON packets(sourcemac)",
                "CREATE TABLE IF NOT EXISTS snapshots (ts_sec INTEGER, ts_usec INTEGER, lat REAL, lon REAL, snaptype TEXT, json TEXT)",
            };

            await conn.RunInTransactionAsync(c =>
            {
                foreach (var sql in schemaStatements)
                    c.Execute(sql);

                int packetId = 1;
                foreach (var ap in accessPoints)
                {
                    var deviceJson = GenerateKismetDeviceJson(ap);
                    string jsonString = deviceJson.ToJsonString();

                    long firstTime = ToUnixTime(ap.FirstSeen);
                    long lastTime = ToUnixTime(ap.LastSeen);
                    string devKey = ap.Bssid ?? "";
                    string phyName = "IEEE802.11";
                    string devMac = ap.Bssid ?? "";
                    int strongestSignal = ap.HighestRssi ?? ap.Rssi ?? -100;

                    double lat = ap.Latitude ?? 0;
                    double lon = ap.Longitude ?? 0;

                    string type = ap.NetworkType == NetworkType.Adhoc ? "Wi-Fi Ad-Hoc" : "Wi-Fi AP";

                    c.Execute(@"
                        INSERT INTO devices (first_time, last_time, devkey, phyname, devmac, strongest_signal, min_lat, min_lon, max_lat, max_lon, avg_lat, avg_lon, bytes_data, type, device)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?);",
                        firstTime, lastTime, devKey, phyName, devMac, strongestSignal, lat, lon, lat, lon, lat, lon, type, jsonString);

                    foreach (var hist in ap.SignalHistory)
                    {
                        // Vistumbler Hist table: Signal (%), RSSI (dBm). Kismet packets expect dBm in 'signal'.
                        int signalDbm = hist.Rssi ?? 0;
                        if (signalDbm == 0 && hist.Signal > 0)
                        {
                            signalDbm = (hist.Signal / 2) - 100;
                        }

                        string tags = $"VISTUMBLER_SIG={hist.Signal}";

                        long pktTime = ToUnixTime(hist.Timestamp);
                        double pktLat = hist.Latitude ?? 0;
                        double pktLon = hist.Longitude ?? 0;
                        double freq = GetFreqFromChannel(ap.Channel);

                        c.Execute(@"
                            INSERT INTO packets (ts_sec, ts_usec, phyname, sourcemac, destmac, transmac, frequency, devkey, lat, lon, alt, speed, heading, packet_len, signal, datasource, dlt, packet, error, tags, datarate, hash, packetid, packet_full_len)
                            VALUES (?, 0, 'IEEE802.11', ?, 'FF:FF:FF:FF:FF:FF', ?, ?, '', ?, ?, 0, 0, 0, 0, ?, 'vistumbler', 127, X'', 0, ?, 0, 0, ?, 0);",
                            pktTime, devMac, devMac, freq, pktLat, pktLon, signalDbm, tags, packetId++);
                    }
                }
            });
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private JsonObject GenerateKismetDeviceJson(AccessPoint ap)
    {
        string ssid = ap.Ssid ?? "";
        int ssidCsum = 0;
        foreach (char c in ssid)
        {
            ssidCsum = (ssidCsum * 31) ^ (int)c;
        }
        ssidCsum = Math.Abs(ssidCsum);
        string ssidKey = ssidCsum.ToString();

        int cryptSet = 0;
        if (ap.Encryption != EncryptionType.None) cryptSet = 2;

        var ssidRecord = new JsonObject
        {
            ["dot11.advertisedssid.ssid"] = ssid,
            ["dot11.advertisedssid.ssidlen"] = ssid.Length,
            ["dot11.advertisedssid.ssid_len"] = ssid.Length,
            ["dot11.advertisedssid.length"] = ssid.Length,
            ["dot11.advertisedssid.crypt_set"] = cryptSet,
            ["dot11.advertisedssid.crypt_bitfield"] = cryptSet,
            ["dot11.advertisedssid.channel"] = ap.Channel.ToString(),
            ["dot11.advertisedssid.beacon_info"] = "",
            ["dot11.advertisedssid.first_time"] = 0,
            ["dot11.advertisedssid.last_time"] = 0
        };

        var ssidMap = new JsonObject { [ssidKey] = ssidRecord };

        var dot11 = new JsonObject
        {
            ["dot11.device.last_beaconed_ssid"] = ssid,
            ["dot11.device.last_beaconed_ssid_record"] = ssidRecord,
            ["dot11.device.last_beaconed_ssid_checksum"] = ssidCsum,
            ["dot11.device.num_advertised_ssids"] = 1,
            ["dot11.device.advertised_ssid_map"] = ssidMap
        };

        int freqKhz = GetFreqFromChannel(ap.Channel) * 1000;
        var freqMap = new JsonObject { [freqKhz.ToString()] = 1 };

        var device = new JsonObject
        {
            ["kismet.device.base.key"] = ap.Bssid ?? "",
            ["kismet.device.base.macaddr"] = ap.Bssid ?? "",
            ["kismet.device.base.name"] = ssid,
            ["kismet.device.base.phyname"] = "IEEE802.11",
            ["kismet.device.base.manuf"] = ap.Manufacturer ?? "Unknown",
            ["kismet.device.base.channel"] = ap.Channel.ToString(),
            ["kismet.device.base.frequency"] = freqKhz,
            ["kismet.device.base.freq_khz_map"] = freqMap,
            ["kismet.device.base.crypt_string"] = ap.Encryption.ToString(),
            ["kismet.device.base.type"] = ap.NetworkType == NetworkType.Adhoc ? "Wi-Fi Ad-Hoc" : "Wi-Fi AP",
            ["kismet.device.base.commonname"] = ssid,
            ["dot11.device"] = dot11,
            ["vistumbler.device.radio_type"] = ap.RadioType ?? "",
            ["vistumbler.device.signal_quality"] = ap.Signal ?? 0
        };

        return device;
    }

    private long ToUnixTime(DateTime date) => new DateTimeOffset(date).ToUnixTimeSeconds();

    private ulong GetChannelBitMask(int channel)
    {
        if (channel >= 1 && channel <= 14) return (ulong)1 << (channel - 1);

        switch (channel)
        {
            case 34: return 0x80000000;
            case 36: return 0x00008000;
            case 38: return 0x08000000;
            case 40: return 0x00010000;
            case 42: return 0x100000000;
            case 44: return 0x00020000;
            case 46: return 0x10000000;
            case 48: return 0x00040000;
            case 52: return 0x00080000;
            case 54: return 0x20000000;
            case 56: return 0x00100000;
            case 60: return 0x00200000;
            case 62: return 0x40000000;
            case 64: return 0x00400000;
            case 149: return 0x00800000;
            case 153: return 0x01000000;
            case 157: return 0x02000000;
            case 161: return 0x04000000;
            default: return 0;
        }
    }

    private long ToFileTimeSafe(DateTime date)
    {
        try
        {
            if (date.Year < 1601) return 0;
            return date.ToFileTimeUtc();
        }
        catch
        {
            return 0;
        }
    }

    public async Task ExportToNetXmlAsync(string filePath, List<AccessPoint> accessPoints)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteDocTypeAsync("detection-run", "SYSTEM", "http://kismetwireless.net/kismet-3.1.0.dtd", null);

        await writer.WriteStartElementAsync(null, "detection-run", null);
        await writer.WriteAttributeStringAsync(null, "kismet-version", null, "Vistumbler");
        await writer.WriteAttributeStringAsync(null, "start-time", null, FormatKismetDate(DateTime.Now));

        await writer.WriteStartElementAsync(null, "card-source", null);
        await writer.WriteAttributeStringAsync(null, "uuid", null, "00000000-0000-0000-0000-000000000000");
        await writer.WriteStringAsync("vistumbler");
        await writer.WriteEndElementAsync(); // card-source

        foreach (var ap in accessPoints)
        {
            await WriteNetXmlNetworkAsync(writer, ap);
        }

        await writer.WriteEndElementAsync(); // detection-run
        await writer.WriteEndDocumentAsync();
    }

    private async Task WriteNetXmlNetworkAsync(XmlWriter writer, AccessPoint ap)
    {
        int minRssi = ap.Rssi ?? -100, maxRssi = ap.HighestRssi ?? ap.Rssi ?? -100, lastRssi = ap.Rssi ?? -100;

        string type = ap.NetworkType == NetworkType.Adhoc ? "ad-hoc" : "infrastructure";
        string startTime = FormatKismetDate(ap.FirstSeen);
        string endTime = FormatKismetDate(ap.LastSeen);

        await writer.WriteStartElementAsync(null, "wireless-network", null);
        await writer.WriteAttributeStringAsync(null, "number", null, "0");
        await writer.WriteAttributeStringAsync(null, "type", null, type);
        await writer.WriteAttributeStringAsync(null, "first-time", null, startTime);
        await writer.WriteAttributeStringAsync(null, "last-time", null, endTime);

        await writer.WriteStartElementAsync(null, "SSID", null);
        await writer.WriteAttributeStringAsync(null, "first-time", null, startTime);
        await writer.WriteAttributeStringAsync(null, "last-time", null, endTime);

        await writer.WriteElementStringAsync(null, "type", null, "Beacon");
        await writer.WriteElementStringAsync(null, "max-rate", null, "54.0");
        await writer.WriteElementStringAsync(null, "packets", null, "0");
        await writer.WriteElementStringAsync(null, "beaconrate", null, "10");

        string encStr = ap.Encryption.ToString();
        if (ap.Encryption == EncryptionType.None) encStr = "None";
        else if (ap.Encryption == EncryptionType.WEP) encStr = "WEP";
        else if (ap.Authentication.ToString().Contains("WPA")) encStr = ap.Authentication.ToString().Replace("_", "+");

        await writer.WriteElementStringAsync(null, "encryption", null, encStr);

        await writer.WriteStartElementAsync(null, "essid", null);
        await writer.WriteAttributeStringAsync(null, "cloaked", null, string.IsNullOrEmpty(ap.Ssid) ? "true" : "false");
        await writer.WriteStringAsync(ap.Ssid);
        await writer.WriteEndElementAsync(); // essid

        await writer.WriteEndElementAsync(); // SSID

        await writer.WriteElementStringAsync(null, "BSSID", null, ap.Bssid);
        await writer.WriteElementStringAsync(null, "manuf", null, ap.Manufacturer);
        await writer.WriteElementStringAsync(null, "channel", null, ap.Channel.ToString());
        await writer.WriteElementStringAsync(null, "freqmhz", null, $"{GetFreqFromChannel(ap.Channel)} 0");
        await writer.WriteElementStringAsync(null, "maxseenrate", null, "54.0");

        await writer.WriteStartElementAsync(null, "snr-info", null);
        await writer.WriteElementStringAsync(null, "last_signal_dbm", null, lastRssi.ToString());
        await writer.WriteElementStringAsync(null, "last_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "last_signal_rssi", null, lastRssi.ToString());
        await writer.WriteElementStringAsync(null, "last_noise_rssi", null, "0");
        await writer.WriteElementStringAsync(null, "min_signal_dbm", null, minRssi.ToString());
        await writer.WriteElementStringAsync(null, "min_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "min_signal_rssi", null, minRssi.ToString());
        await writer.WriteElementStringAsync(null, "min_noise_rssi", null, "0");
        await writer.WriteElementStringAsync(null, "max_signal_dbm", null, maxRssi.ToString());
        await writer.WriteElementStringAsync(null, "max_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "max_signal_rssi", null, maxRssi.ToString());
        await writer.WriteElementStringAsync(null, "max_noise_rssi", null, "0");
        await writer.WriteEndElementAsync(); // snr-info

        if (!string.IsNullOrEmpty(ap.RadioType))
        {
            await writer.WriteStartElementAsync(null, "bsstype", null);
            await writer.WriteStringAsync(ap.RadioType);
            await writer.WriteEndElementAsync();
        }

        if (ap.Signal.HasValue)
        {
            await writer.WriteStartElementAsync(null, "signal_quality", null);
            await writer.WriteStringAsync(ap.Signal.Value.ToString());
            await writer.WriteEndElementAsync();
        }

        if (ap.Latitude.HasValue || ap.Longitude.HasValue)
        {
            double lat = ap.Latitude ?? 0;
            double lon = ap.Longitude ?? 0;
            await writer.WriteStartElementAsync(null, "gps-info", null);
            await writer.WriteElementStringAsync(null, "min-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "min-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "min-alt", null, "0");
            await writer.WriteElementStringAsync(null, "min-spd", null, "0");
            await writer.WriteElementStringAsync(null, "max-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "max-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "max-alt", null, "0");
            await writer.WriteElementStringAsync(null, "max-spd", null, "0");
            await writer.WriteElementStringAsync(null, "peak-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "peak-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "peak-alt", null, "0");
            await writer.WriteElementStringAsync(null, "peak-spd", null, "0");
            await writer.WriteElementStringAsync(null, "avg-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "avg-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "avg-alt", null, "0");
            await writer.WriteElementStringAsync(null, "avg-spd", null, "0");
            await writer.WriteEndElementAsync(); // gps-info
        }

        await writer.WriteElementStringAsync(null, "datasize", null, "0");
        await writer.WriteEndElementAsync(); // wireless-network
    }

    private string FormatKismetDate(DateTime dt) =>
        dt.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private int GetFreqFromChannel(int channel)
    {
        if (channel >= 1 && channel <= 13) return 2407 + (channel * 5);
        if (channel == 14) return 2484;
        if (channel >= 36 && channel <= 177) return 5000 + (channel * 5);
        return 0;
    }

    public async Task ExportToKmlAsync(string filePath, List<AccessPoint> accessPoints, ExportOptions options)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "kml", "http://www.opengis.net/kml/2.2");
        await writer.WriteStartElementAsync(null, "Document", null);

        await writer.WriteElementStringAsync(null, "name", null, "Vistumbler WiFi Scan");

        await WriteKmlStylesAsync(writer);

        var filteredAps = FilterAccessPoints(accessPoints, options);

        foreach (var ap in filteredAps)
        {
            if (ap.Latitude.HasValue && ap.Longitude.HasValue)
            {
                await WritePlacemarkAsync(writer, ap, options);
            }
        }

        await writer.WriteEndElementAsync(); // Document
        await writer.WriteEndElementAsync(); // kml
        await writer.WriteEndDocumentAsync();
    }

    public async Task ExportToGpxAsync(string filePath, List<AccessPoint> accessPoints)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "gpx", "http://www.topografix.com/GPX/1/1");
        await writer.WriteAttributeStringAsync(null, "version", null, "1.1");
        await writer.WriteAttributeStringAsync(null, "creator", null, "VistumblerMAUI");

        foreach (var ap in accessPoints.Where(a => a.Latitude.HasValue && a.Longitude.HasValue))
        {
            await writer.WriteStartElementAsync(null, "wpt", null);
            await writer.WriteAttributeStringAsync(null, "lat", null, ap.Latitude!.Value.ToString("F6"));
            await writer.WriteAttributeStringAsync(null, "lon", null, ap.Longitude!.Value.ToString("F6"));

            await writer.WriteElementStringAsync(null, "name", null, ap.Ssid);
            await writer.WriteElementStringAsync(null, "desc", null,
                $"BSSID: {ap.Bssid}, Signal: {ap.Signal}%, Channel: {ap.Channel}");

            await writer.WriteEndElementAsync(); // wpt
        }

        await writer.WriteEndElementAsync(); // gpx
        await writer.WriteEndDocumentAsync();
    }

    public async Task ExportToCsvAsync(string filePath, List<AccessPoint> accessPoints, bool includeSignalHistory = false)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        await writer.WriteLineAsync(
            "BSSID,SSID,Channel,Authentication,Encryption,NetworkType,Signal,HighSignal,RSSI,HighRSSI," +
            "RadioType,Manufacturer,Label,Latitude,Longitude,FirstSeen,LastSeen");

        foreach (var ap in accessPoints)
        {
            var line = $"{EscapeCsv(ap.Bssid)},{EscapeCsv(ap.Ssid)},{ap.Channel}," +
                      $"{ap.Authentication.ToLegacyString()},{ap.Encryption.ToLegacyString()},{ap.NetworkType}," +
                      $"{ap.Signal},{ap.HighestSignal},{ap.Rssi},{ap.HighestRssi}," +
                      $"{EscapeCsv(ap.RadioType)},{EscapeCsv(ap.Manufacturer)},{EscapeCsv(ap.Label)}," +
                      $"{ap.Latitude},{ap.Longitude}," +
                      $"{ap.FirstSeen:yyyy-MM-dd HH:mm:ss},{ap.LastSeen:yyyy-MM-dd HH:mm:ss}";

            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportToWigleCsvAsync(string filePath, List<AccessPoint> accessPoints)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        await writer.WriteLineAsync("WigleWifi-1.6,appRelease=VistumblerMAUI,model=PC,release=1.0,device=PC,display=,board=,brand=");
        await writer.WriteLineAsync("MAC,SSID,AuthMode,FirstSeen,Channel,Frequency,RSSI,CurrentLatitude,CurrentLongitude,AltitudeMeters,AccuracyMeters,Type");

        foreach (var ap in accessPoints)
        {
            string authMode = BuildWigleAuthMode(ap.Authentication, ap.Encryption, ap.NetworkType);
            int freq = GetFreqFromChannel(ap.Channel);

            var line = string.Join(",",
                EscapeCsv(ap.Bssid),
                EscapeCsv(ap.Ssid),
                authMode,
                ap.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
                ap.Channel,
                freq,
                ap.Rssi?.ToString() ?? "",
                ap.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ap.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                "0",
                "0",
                "WIFI");
            await writer.WriteLineAsync(line);
        }
    }

    /// <summary>
    /// Port of _WigleCSV_BuildAuthMode: converts Vistumbler Authentication/Encryption/NetworkType
    /// to WiGLE AuthMode capability flags.
    /// </summary>
    private string BuildWigleAuthMode(AuthenticationType auth, EncryptionType encr, NetworkType netType)
    {
        string encrNorm;
        if (encr == EncryptionType.GCMP || encr == EncryptionType.GCMP_256)
            encrNorm = "GCMP";
        else if (encr == EncryptionType.CCMP || encr == EncryptionType.CCMP_256 || encr == EncryptionType.AES)
            encrNorm = "CCMP";
        else
            encrNorm = encr.ToLegacyString();

        string flags;
        if (auth == AuthenticationType.WPA3_Enterprise || auth == AuthenticationType.WPA3_Enterprise_192)
            flags = $"[WPA3-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA3_PSK || auth == AuthenticationType.WPA3)
            flags = $"[WPA3-SAE-{encrNorm}]";
        else if (auth == AuthenticationType.WPA2_Enterprise)
            flags = $"[WPA2-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA2_PSK || auth == AuthenticationType.WPA2)
            flags = $"[WPA2-PSK-{encrNorm}]";
        else if (auth == AuthenticationType.WPA_Enterprise)
            flags = $"[WPA-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA_PSK || auth == AuthenticationType.WPA)
            flags = $"[WPA-PSK-{encrNorm}]";
        else if (auth == AuthenticationType.OWE)
            flags = "[OWE]";
        else if ((auth == AuthenticationType.Open || auth == AuthenticationType.Shared) && encr == EncryptionType.WEP)
            flags = "[WEP]";
        else if (auth == AuthenticationType.Open && encr == EncryptionType.None)
            return "";
        else
            flags = "";

        if (flags != "")
        {
            flags += netType == NetworkType.Adhoc ? "[IBSS]" : "[ESS]";
        }

        return flags;
    }

    public async Task ExportToVs1Async(string filePath, List<AccessPoint> accessPoints)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        await writer.WriteLineAsync("# Vistumbler VS1 File");
        await writer.WriteLineAsync($"# Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync("# GpsID|Latitude|Longitude|NumOfSats|HorDilPitch|Alt|Geo|SpeedInMPH|SpeedInKmH|TrackAngle|Date|Time");
        await writer.WriteLineAsync("# APID|BSSID|SSID|CHAN|AUTH|ENCR|RADTYPE|BTX|OTX|NETTYPE|Signal|HighSignal|RSSI|HighRSSI|Latitude|Longitude|FirstSeen|LastSeen|Manufacturer|Label");
        await writer.WriteLineAsync("# HistID|APID|GpsID|Signal|RSSI|Date|Time");

        foreach (var ap in accessPoints)
        {
            var line = $"AP|{ap.ApId}|{ap.Bssid}|{ap.Ssid}|{ap.Channel}|{ap.Authentication.ToLegacyString()}|{ap.Encryption.ToLegacyString()}|" +
                      $"{ap.RadioType}|{ap.BasicTransferRates}|{ap.OtherTransferRates}|{ap.NetworkType}|" +
                      $"{ap.Signal}|{ap.HighestSignal}|{ap.Rssi}|{ap.HighestRssi}|" +
                      $"{ap.Latitude}|{ap.Longitude}|{ap.FirstSeen:yyyy-MM-dd HH:mm:ss}|{ap.LastSeen:yyyy-MM-dd HH:mm:ss}|" +
                      $"{ap.Manufacturer}|{ap.Label}";

            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportToVszAsync(string filePath, List<AccessPoint> accessPoints)
    {
        var tempVs1 = Path.GetTempFileName();
        try
        {
            await ExportToVs1Async(tempVs1, accessPoints);

            if (File.Exists(filePath))
                File.Delete(filePath);

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            var entryName = Path.GetFileNameWithoutExtension(filePath) + ".vs1";
            archive.CreateEntryFromFile(tempVs1, entryName);
        }
        finally
        {
            if (File.Exists(tempVs1))
                File.Delete(tempVs1);
        }
    }

    private List<AccessPoint> FilterAccessPoints(List<AccessPoint> accessPoints, ExportOptions options)
    {
        return accessPoints.Where(ap =>
        {
            if (ap.Encryption == EncryptionType.None && !options.IncludeOpenNetworks)
                return false;

            if (ap.Encryption == EncryptionType.WEP && !options.IncludeWepNetworks)
                return false;

            if ((ap.Encryption == EncryptionType.TKIP || ap.Encryption == EncryptionType.AES) && !options.IncludeSecureNetworks)
                return false;

            return true;
        }).ToList();
    }

    private async Task WriteKmlStylesAsync(XmlWriter writer)
    {
        var signalColors = new[]
        {
            ("VeryLow", "ff0000ff"),
            ("Low", "ff0055ff"),
            ("Medium", "ff00ffff"),
            ("Good", "ff01ffc8"),
            ("Excellent", "ff70ff48")
        };

        foreach (var (name, color) in signalColors)
        {
            await writer.WriteStartElementAsync(null, "Style", null);
            await writer.WriteAttributeStringAsync(null, "id", null, $"Signal{name}");

            await writer.WriteStartElementAsync(null, "IconStyle", null);
            await writer.WriteElementStringAsync(null, "color", null, color);
            await writer.WriteEndElementAsync(); // IconStyle

            await writer.WriteEndElementAsync(); // Style
        }
    }

    private async Task WritePlacemarkAsync(XmlWriter writer, AccessPoint ap, ExportOptions options)
    {
        await writer.WriteStartElementAsync(null, "Placemark", null);

        await writer.WriteElementStringAsync(null, "name", null, string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : ap.Ssid);

        var description = $"BSSID: {ap.Bssid}\n" +
                         $"Channel: {ap.Channel}\n" +
                         $"Signal: {ap.Signal}%\n" +
                         $"RSSI: {ap.Rssi} dBm\n" +
                         $"Authentication: {ap.Authentication}\n" +
                         $"Encryption: {ap.Encryption}\n" +
                         $"Manufacturer: {ap.Manufacturer}";

        await writer.WriteElementStringAsync(null, "description", null, description);

        if (options.UseSignalColors)
        {
            var styleId = GetSignalStyleId(ap.Signal ?? 0);
            await writer.WriteElementStringAsync(null, "styleUrl", null, $"#{styleId}");
        }

        await writer.WriteStartElementAsync(null, "Point", null);
        await writer.WriteElementStringAsync(null, "coordinates", null,
            $"{ap.Longitude},{ap.Latitude},0");
        await writer.WriteEndElementAsync(); // Point

        await writer.WriteEndElementAsync(); // Placemark
    }

    private string GetSignalStyleId(int signal)
    {
        return signal switch
        {
            >= 80 => "SignalExcellent",
            >= 60 => "SignalGood",
            >= 40 => "SignalMedium",
            >= 20 => "SignalLow",
            _ => "SignalVeryLow"
        };
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
