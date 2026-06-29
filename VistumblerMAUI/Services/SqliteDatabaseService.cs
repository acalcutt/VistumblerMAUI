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
    private SQLiteAsyncConnection? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string DbPath => Path.Combine(
        FileSystem.AppDataDirectory, "vistumbler.db3");

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
            await _db.UpdateAsync(row);
        }
        return ap.ApId;
    }

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
    }

    // ── Signal History ────────────────────────────────────────────────────────
    public async Task AddSignalHistoryAsync(SignalHistory entry)
    {
        await _db!.InsertAsync(DbSignalHistory.FromModel(entry));
    }

    public async Task<List<SignalHistory>> GetSignalHistoryAsync(int apId)
    {
        var rows = await _db!.Table<DbSignalHistory>().Where(r => r.ApId == apId).ToListAsync();
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
                                    public int?    Signal       { get; set; }
                                    public int?    HighestSignal{ get; set; }
                                    public int?    Rssi         { get; set; }
                                    public int?    HighestRssi  { get; set; }
                                    public long    FirstSeen    { get; set; }
                                    public long    LastSeen     { get; set; }
                                    public double? Latitude     { get; set; }
                                    public double? Longitude    { get; set; }

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
            Longitude     = m.Longitude
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
            FirstSeen     = new DateTime(FirstSeen, DateTimeKind.Utc),
            LastSeen      = new DateTime(LastSeen,  DateTimeKind.Utc),
            Latitude      = Latitude,
            Longitude     = Longitude
        };
    }

    [Table("SignalHistory")]
    private class DbSignalHistory
    {
        [PrimaryKey, AutoIncrement] public int     Id        { get; set; }
        [Indexed]                   public int     ApId      { get; set; }
                                    public int     Signal    { get; set; }
                                    public int?    Rssi      { get; set; }
                                    public double? Latitude  { get; set; }
                                    public double? Longitude { get; set; }
                                    public long    Timestamp { get; set; }

        public static DbSignalHistory FromModel(SignalHistory m) => new()
        {
            Id        = m.Id,
            ApId      = m.ApId,
            Signal    = m.Signal,
            Rssi      = m.Rssi,
            Latitude  = m.Latitude,
            Longitude = m.Longitude,
            Timestamp = m.Timestamp.Ticks
        };

        public SignalHistory ToModel() => new()
        {
            Id        = Id,
            ApId      = ApId,
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
