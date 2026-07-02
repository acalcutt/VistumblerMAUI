using SQLite;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// SQLite database service using sqlite-net-pcl (async API).
/// Stores access points, signal history, GPS track, and OUI lookups.
/// </summary>
public class SqliteDatabaseService : IDatabaseService
{
    private readonly ISessionService _session;
    private SQLiteAsyncConnection? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SqliteDatabaseService(ISessionService session) => _session = session;

    // Each session is its own timestamped database file (VistumblerMDB-style).
    private string DbPath
    {
        get
        {
            if (string.IsNullOrEmpty(_session.CurrentSessionPath))
                _session.StartNewSession();
            return _session.CurrentSessionPath;
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _db = new SQLiteAsyncConnection(DbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.CreateTableAsync<DbAccessPoint>();
            await _db.CreateTableAsync<DbSignalHistory>();
            await _db.CreateTableAsync<DbGpsData>();
            await _db.CreateTableAsync<DbManufacturer>();
            await _db.CreateTableAsync<DbLabel>();
            _initialized = true;
        }
        finally { _initLock.Release(); }
    }

    /// <summary>Close the current database connection so its file can be deleted/switched.</summary>
    public async Task CloseAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_db is not null)
            {
                await _db.CloseAsync();
                _db = null;
            }
            _initialized = false;
        }
        finally { _initLock.Release(); }
    }

    // ── Access Points ─────────────────────────────────────────────────────────
    public async Task<int> UpsertAccessPointAsync(AccessPoint ap)
    {
        var row = DbAccessPoint.FromModel(ap);
        var existing = await _db!.FindAsync<DbAccessPoint>(x => x.Bssid == ap.Bssid);
        if (existing is null)
        {
            await _db.InsertAsync(row);
            ap.ApId = row.Id;
        }
        else
        {
            row.Id = existing.Id;
            ap.ApId = existing.Id;
            // Don't overwrite stored history links with missing (0) values.
            if (row.FirstHistId      == 0) row.FirstHistId      = existing.FirstHistId;
            if (row.LastHistId       == 0) row.LastHistId       = existing.LastHistId;
            if (row.HighSignalHistId == 0) row.HighSignalHistId = existing.HighSignalHistId;
            if (row.HighRssiHistId   == 0) row.HighRssiHistId   = existing.HighRssiHistId;
            await _db.UpdateAsync(row);
        }
        return ap.ApId;
    }

    public async Task UpdateApHistLinksAsync(AccessPoint ap)
    {
        await _db!.ExecuteAsync(
            "UPDATE AccessPoints SET FirstHistId=?, LastHistId=?, HighSignalHistId=?, HighRssiHistId=? WHERE Id=?",
            ap.FirstHistId, ap.LastHistId, ap.HighSignalHistId, ap.HighRssiHistId, ap.ApId);
    }

    public async Task SaveScanCycleAsync(IReadOnlyList<AccessPoint> aps, GpsData? gps, DateTime scanTime)
    {
        if (aps.Count == 0) return;
        await InitializeAsync();

        await _db!.RunInTransactionAsync(conn =>
        {
            // One GPS row per cycle, shared by every sample this cycle (0 = no fix). Stamp it
            // with the scan time (which advances each cycle) rather than the fix's own
            // timestamp — a stationary device's continuous-location listener reports one fix
            // with a fixed timestamp, which would otherwise make every cycle's GPS row identical
            // (and collapse to a single point on import). This also matches the HIST timestamp.
            int gpsId = 0;
            if (gps is not null)
            {
                var g = new DbGpsData
                {
                    Latitude   = gps.Latitude,
                    Longitude  = gps.Longitude,
                    Altitude   = gps.Altitude,
                    SpeedKnots = gps.SpeedKnots,
                    TrackAngle = gps.TrackAngle,
                    Timestamp  = scanTime.Ticks,
                    Quality    = (int)gps.Quality
                };
                conn.Insert(g);
                gpsId = g.Id;
            }

            foreach (var ap in aps)
            {
                // Upsert AP core + denormalized current/high values.
                var row = DbAccessPoint.FromModel(ap);
                var existing = conn.Table<DbAccessPoint>().FirstOrDefault(x => x.Bssid == ap.Bssid);
                if (existing is null) { conn.Insert(row); ap.ApId = row.Id; }
                else                  { row.Id = existing.Id; ap.ApId = existing.Id; conn.Update(row); }

                // Append this cycle's HIST sample and maintain the AP → HIST links.
                var h = new DbSignalHistory
                {
                    ApId = ap.ApId, GpsId = gpsId,
                    Signal = ap.Signal ?? 0, Rssi = ap.Rssi, Timestamp = scanTime.Ticks
                };
                conn.Insert(h);

                if (ap.FirstHistId == 0) ap.FirstHistId = h.Id;
                ap.LastHistId = h.Id;
                if ((ap.Signal ?? int.MinValue) >= (ap.HighestSignal ?? int.MinValue)) ap.HighSignalHistId = h.Id;
                if (ap.Rssi.HasValue && ap.Rssi.Value >= (ap.HighestRssi ?? int.MinValue)) ap.HighRssiHistId = h.Id;

                conn.Execute("UPDATE AccessPoints SET FirstHistId=?, LastHistId=?, HighSignalHistId=?, HighRssiHistId=? WHERE Id=?",
                    ap.FirstHistId, ap.LastHistId, ap.HighSignalHistId, ap.HighRssiHistId, ap.ApId);
            }
        });
    }

    // The AP list reads the AccessPoints table directly (no joins) — the current/high
    // values and first/last timestamps are denormalized onto the row, matching the
    // original VistumblerMDB AP table, so a full list load is a single-table scan. The
    // HIST/GPS tables + link ids on the row serve per-AP detail (history graph, exact fixes).
    public async Task<AccessPoint?> GetAccessPointByBssidAsync(string bssid)
    {
        var row = await _db!.FindAsync<DbAccessPoint>(x => x.Bssid == bssid);
        return row?.ToModel();
    }

    public async Task<List<AccessPoint>> GetAllAccessPointsAsync()
    {
        var rows = await _db!.Table<DbAccessPoint>().ToListAsync();
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task ClearAllAccessPointsAsync()
    {
        await _db!.DeleteAllAsync<DbAccessPoint>();
        await _db.DeleteAllAsync<DbSignalHistory>();
        await _db.DeleteAllAsync<DbGpsData>();
    }

    // Recompute one AP's first/last/high history links across all its HIST rows.
    private const string ApLinkBackfillSql =
        @"UPDATE AccessPoints SET
            FirstHistId      = COALESCE((SELECT Id FROM SignalHistory WHERE ApId=? ORDER BY Timestamp ASC,  Id ASC  LIMIT 1), 0),
            LastHistId       = COALESCE((SELECT Id FROM SignalHistory WHERE ApId=? ORDER BY Timestamp DESC, Id DESC LIMIT 1), 0),
            HighSignalHistId = COALESCE((SELECT Id FROM SignalHistory WHERE ApId=? ORDER BY Signal DESC,    Id DESC LIMIT 1), 0),
            HighRssiHistId   = COALESCE((SELECT Id FROM SignalHistory WHERE ApId=? AND Rssi IS NOT NULL ORDER BY Rssi DESC, Id DESC LIMIT 1), 0)
          WHERE Id=?";

    public async Task ImportAccessPointsAsync(IReadOnlyList<AccessPoint> aps)
    {
        await InitializeAsync();

        // One transaction for the whole import — far faster, and all-or-nothing.
        await _db!.RunInTransactionAsync(conn =>
        {
            foreach (var ap in aps)
            {
                // Upsert the AP core (translating the source record into our AP row).
                var row = DbAccessPoint.FromModel(ap);
                var existing = conn.Table<DbAccessPoint>().FirstOrDefault(x => x.Bssid == ap.Bssid);
                if (existing is null)
                {
                    conn.Insert(row);
                    ap.ApId = row.Id;
                }
                else
                {
                    row.Id = existing.Id;
                    ap.ApId = existing.Id;
                    // Keep existing history links; the backfill below recomputes them across
                    // the merged (existing + imported) history for this AP.
                    row.FirstHistId      = existing.FirstHistId;
                    row.LastHistId       = existing.LastHistId;
                    row.HighSignalHistId = existing.HighSignalHistId;
                    row.HighRssiHistId   = existing.HighRssiHistId;
                    conn.Update(row);
                }

                // Translate the source's per-observation samples into HIST rows, each with a
                // GPS row when it carried a position. If the source gave no samples, synthesize
                // one from the AP summary so first/last/signal still resolve (no flat columns).
                var samples = ap.SignalHistory.Count > 0
                    ? ap.SignalHistory.OrderBy(h => h.Timestamp).ToList()
                    : new List<SignalHistory>
                      {
                          new()
                          {
                              Signal    = ap.Signal ?? 0,
                              Rssi      = ap.Rssi,
                              Latitude  = ap.Latitude,
                              Longitude = ap.Longitude,
                              Timestamp = ap.LastSeen == default ? ap.FirstSeen : ap.LastSeen
                          }
                      };

                foreach (var hist in samples)
                {
                    int gpsId = 0;
                    if (hist.Latitude.HasValue && hist.Longitude.HasValue)
                    {
                        var g = new DbGpsData
                        {
                            Latitude  = hist.Latitude.Value,
                            Longitude = hist.Longitude.Value,
                            Timestamp = hist.Timestamp.Ticks,
                            Quality   = 1
                        };
                        conn.Insert(g);
                        gpsId = g.Id;
                    }

                    conn.Insert(new DbSignalHistory
                    {
                        ApId      = ap.ApId,
                        GpsId     = gpsId,
                        Signal    = hist.Signal,
                        Rssi      = hist.Rssi,
                        Timestamp = hist.Timestamp.Ticks
                    });
                }

                // Recompute links across all of this AP's history (existing + imported).
                conn.Execute(ApLinkBackfillSql, ap.ApId, ap.ApId, ap.ApId, ap.ApId, ap.ApId);
            }
        });
    }

    // ── Signal History ────────────────────────────────────────────────────────
    public async Task<int> AddSignalHistoryAsync(SignalHistory entry)
    {
        var row = DbSignalHistory.FromModel(entry);
        await _db!.InsertAsync(row);
        entry.Id = row.Id;
        return row.Id;
    }

    public async Task<List<SignalHistory>> GetSignalHistoryAsync(int apId)
    {
        // Each sample's position comes from its linked GPS row (null when there was no fix).
        var rows = await _db!.QueryAsync<HistRow>(
            @"SELECT h.Id, h.ApId, h.GpsId, h.Signal, h.Rssi, h.Timestamp,
                     g.Latitude AS Latitude, g.Longitude AS Longitude
              FROM SignalHistory h
              LEFT JOIN GpsData g ON g.Id = h.GpsId
              WHERE h.ApId = ? ORDER BY h.Timestamp ASC, h.Id ASC", apId);
        return rows.Select(r => r.ToModel()).ToList();
    }

    // ── GPS Track ─────────────────────────────────────────────────────────────
    public async Task<int> AddGpsDataAsync(GpsData gps)
    {
        var row = DbGpsData.FromModel(gps);
        await _db!.InsertAsync(row);
        gps.GpsId = row.Id;
        return row.Id;
    }

    public async Task<List<GpsData>> GetAllGpsAsync()
    {
        var rows = await _db!.Table<DbGpsData>().ToListAsync();
        return rows.Select(r => new GpsData
        {
            GpsId              = r.Id,
            Latitude           = r.Latitude,
            Longitude          = r.Longitude,
            Altitude           = r.Altitude,
            SpeedKnots         = r.SpeedKnots,
            TrackAngle         = r.TrackAngle,
            Timestamp          = new DateTime(r.Timestamp, DateTimeKind.Utc),
            Quality            = (GpsQuality)r.Quality
        }).ToList();
    }

    // ── OUI Manufacturers ─────────────────────────────────────────────────────
    public async Task<string> GetManufacturerAsync(string ouiPrefix)
    {
        var row = await _db!.FindAsync<DbManufacturer>(x => x.OuiPrefix == ouiPrefix.ToUpperInvariant());
        return row?.Manufacturer ?? string.Empty;
    }

    public async Task BulkUpsertManufacturersAsync(IEnumerable<(string OuiPrefix, string Manufacturer)> entries)
    {
        var rows = entries.Select(e => new DbManufacturer
        {
            OuiPrefix    = e.OuiPrefix.ToUpperInvariant(),
            Manufacturer = e.Manufacturer
        }).ToList();
        await _db!.InsertAllAsync(rows, true); // true = INSERT OR REPLACE
    }

    // ── Labels ────────────────────────────────────────────────────────────────
    public async Task<string?> GetLabelAsync(string bssid)
    {
        var row = await _db!.FindAsync<DbLabel>(x => x.Bssid == bssid);
        return row?.UserLabel;
    }

    public async Task SetLabelAsync(string bssid, string label)
    {
        var row = new DbLabel { Bssid = bssid, UserLabel = label };
        await _db!.InsertOrReplaceAsync(row);
    }

    // ── DB Row types ──────────────────────────────────────────────────────────
    [Table("AccessPoints")]
    private class DbAccessPoint
    {
        [PrimaryKey, AutoIncrement] public int     Id           { get; set; }
        [Indexed]                   public string  Bssid        { get; set; } = string.Empty;
                                    public string  Ssid         { get; set; } = string.Empty;
                                    public string  Manufacturer { get; set; } = string.Empty;
                                    public string  RadioType    { get; set; } = string.Empty;
                                    public int     NetworkType  { get; set; }
                                    public int     Auth         { get; set; }
                                    public int     Encryption   { get; set; }
                                    public int     Channel      { get; set; }
                                    public int     FreqMhz      { get; set; }
        // Denormalized current/high values + first/last timestamps + last position, so the
        // list loads from this table alone (matching VistumblerMDB's AP table). Kept in sync
        // by the scan/import as each AP is updated.
                                    public int?    Signal       { get; set; }
                                    public int?    HighestSignal{ get; set; }
                                    public int?    Rssi         { get; set; }
                                    public int?    HighestRssi  { get; set; }
                                    public long    FirstSeen    { get; set; }
                                    public long    LastSeen     { get; set; }
                                    public double? Latitude     { get; set; }
                                    public double? Longitude    { get; set; }
        // HIST/GPS link ids (0 = unset) for per-AP detail: first/last sample, and the
        // samples that set the high signal / high RSSI (each HIST row links to its GPS fix).
                                    public int     FirstHistId      { get; set; }
                                    public int     LastHistId       { get; set; }
                                    public int     HighSignalHistId { get; set; }
                                    public int     HighRssiHistId   { get; set; }

        public static DbAccessPoint FromModel(AccessPoint m) => new()
        {
            Id            = m.ApId,
            Bssid         = m.Bssid,
            Ssid          = m.Ssid,
            Manufacturer  = m.Manufacturer,
            RadioType     = m.RadioType,
            NetworkType   = (int)m.NetworkType,
            Auth          = (int)m.Authentication,
            Encryption    = (int)m.Encryption,
            Channel       = m.Channel,
            FreqMhz       = m.FrequencyMhz,
            Signal        = m.Signal,
            HighestSignal = m.HighestSignal,
            Rssi          = m.Rssi,
            HighestRssi   = m.HighestRssi,
            FirstSeen     = m.FirstSeen.Ticks,
            LastSeen      = m.LastSeen.Ticks,
            Latitude      = m.Latitude,
            Longitude     = m.Longitude,
            FirstHistId      = m.FirstHistId,
            LastHistId       = m.LastHistId,
            HighSignalHistId = m.HighSignalHistId,
            HighRssiHistId   = m.HighRssiHistId
        };

        public AccessPoint ToModel() => new()
        {
            ApId          = Id,
            Bssid         = Bssid,
            Ssid          = Ssid,
            Manufacturer  = Manufacturer,
            RadioType     = RadioType,
            NetworkType   = (NetworkType)NetworkType,
            Authentication= (AuthenticationType)Auth,
            Encryption    = (EncryptionType)Encryption,
            Channel       = Channel,
            FrequencyMhz  = FreqMhz,
            Signal        = Signal,
            HighestSignal = HighestSignal,
            Rssi          = Rssi,
            HighestRssi   = HighestRssi,
            FirstSeen     = FirstSeen == 0 ? default : new DateTime(FirstSeen, DateTimeKind.Utc),
            LastSeen      = LastSeen  == 0 ? default : new DateTime(LastSeen,  DateTimeKind.Utc),
            Latitude      = Latitude,
            Longitude     = Longitude,
            FirstHistId      = FirstHistId,
            LastHistId       = LastHistId,
            HighSignalHistId = HighSignalHistId,
            HighRssiHistId   = HighRssiHistId
        };
    }

    [Table("SignalHistory")]
    private class DbSignalHistory
    {
        [PrimaryKey, AutoIncrement] public int     Id        { get; set; }
        [Indexed]                   public int     ApId      { get; set; }
                                    public int     GpsId     { get; set; }
                                    public int     Signal    { get; set; }
                                    public int?    Rssi      { get; set; }
                                    public long    Timestamp { get; set; }

        public static DbSignalHistory FromModel(SignalHistory m) => new()
        {
            Id        = m.Id,
            ApId      = m.ApId,
            GpsId     = m.GpsId,
            Signal    = m.Signal,
            Rssi      = m.Rssi,
            Timestamp = m.Timestamp.Ticks
        };
    }

    // ── Read projection (raw-SQL join result) ─────────────────────────────────

    // Signal-history sample joined to its GPS fix.
    private class HistRow
    {
        public int     Id { get; set; }
        public int     ApId { get; set; }
        public int     GpsId { get; set; }
        public int     Signal { get; set; }
        public int?    Rssi { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public long    Timestamp { get; set; }

        public SignalHistory ToModel() => new()
        {
            Id        = Id,
            ApId      = ApId,
            GpsId     = GpsId,
            Signal    = Signal,
            Rssi      = Rssi,
            Latitude  = Latitude,
            Longitude = Longitude,
            Timestamp = new DateTime(Timestamp, DateTimeKind.Utc)
        };
    }

    [Table("GpsData")]
    private class DbGpsData
    {
        [PrimaryKey, AutoIncrement] public int     Id         { get; set; }
                                    public double  Latitude   { get; set; }
                                    public double  Longitude  { get; set; }
                                    public double? Altitude   { get; set; }
                                    public double? SpeedKnots { get; set; }
                                    public double? TrackAngle { get; set; }
                                    public long    Timestamp  { get; set; }
                                    public int     Quality    { get; set; }

        public static DbGpsData FromModel(GpsData m) => new()
        {
            Id         = m.GpsId,
            Latitude   = m.Latitude,
            Longitude  = m.Longitude,
            Altitude   = m.Altitude,
            SpeedKnots = m.SpeedKnots,
            TrackAngle = m.TrackAngle,
            Timestamp  = m.Timestamp.Ticks,
            Quality    = (int)m.Quality
        };
    }

    [Table("Manufacturers")]
    private class DbManufacturer
    {
        [PrimaryKey] public string OuiPrefix    { get; set; } = string.Empty;
                     public string Manufacturer { get; set; } = string.Empty;
    }

    [Table("Labels")]
    private class DbLabel
    {
        [PrimaryKey] public string Bssid     { get; set; } = string.Empty;
                     public string UserLabel { get; set; } = string.Empty;
    }
}
