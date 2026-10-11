using SQLite;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

/// <summary>
/// Save &amp; Clear's record of the session: each save, and one point per AP it cleared (where the AP was strongest).
/// Kept in the session database apart from the AP tables, so <see cref="ClearAllAccessPointsAsync"/> leaves them,
/// and a resumed session still has them; a new session starts without.
/// </summary>
public partial class SqliteDatabaseService
{
    public async Task<int> AddSessionSaveAsync(SessionSave save, IReadOnlyList<SavedPoint> points)
    {
        await InitializeAsync();
        var row = DbSessionSave.FromModel(save);
        await _db!.RunInTransactionAsync(conn =>
        {
            conn.Insert(row);
            foreach (var p in points)
                conn.Insert(DbSavedPoint.FromModel(p, row.Id));
        });
        save.Id = row.Id;
        return row.Id;
    }

    public async Task<List<SessionSave>> GetSessionSavesAsync()
    {
        await InitializeAsync();
        var rows = await _db!.Table<DbSessionSave>().OrderBy(s => s.Id).ToListAsync();
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<List<SavedPoint>> GetSavedPointsAsync()
    {
        await InitializeAsync();
        var rows = await _db!.Table<DbSavedPoint>().ToListAsync();
        return rows.Select(r => r.ToModel()).ToList();
    }

    [Table("SessionSaves")]
    private class DbSessionSave
    {
        [PrimaryKey, AutoIncrement] public int    Id       { get; set; }
                                    public long   SavedUtc { get; set; }
                                    public string FileName { get; set; } = string.Empty;
                                    public string Path     { get; set; } = string.Empty;
                                    public int    ApCount  { get; set; }
                                    public string Md5      { get; set; } = string.Empty;
                                    public bool   ToWifiDb { get; set; }

        public static DbSessionSave FromModel(SessionSave m) => new()
        {
            SavedUtc = m.SavedUtc.Ticks, FileName = m.FileName, Path = m.Path,
            ApCount = m.ApCount, Md5 = m.Md5, ToWifiDb = m.ToWifiDb,
        };

        public SessionSave ToModel() => new()
        {
            Id = Id, SavedUtc = new DateTime(SavedUtc, DateTimeKind.Utc), FileName = FileName, Path = Path,
            ApCount = ApCount, Md5 = Md5, ToWifiDb = ToWifiDb,
        };
    }

    [Table("SavedPoints")]
    private class DbSavedPoint
    {
        [PrimaryKey, AutoIncrement] public int    Id             { get; set; }
        [Indexed]                   public int    SaveId         { get; set; }
                                    public string Bssid          { get; set; } = string.Empty;
                                    public string Ssid           { get; set; } = string.Empty;
                                    public string Authentication { get; set; } = string.Empty;
                                    public string Encryption     { get; set; } = string.Empty;
                                    public string RadioType      { get; set; } = string.Empty;
                                    public string Manufacturer   { get; set; } = string.Empty;
                                    public int    Channel        { get; set; }
                                    public int    SecurityType   { get; set; }
                                    public int?   Signal         { get; set; }
                                    public int?   Rssi           { get; set; }
                                    public double Latitude       { get; set; }
                                    public double Longitude      { get; set; }
                                    public long   LastSeen       { get; set; }

        public static DbSavedPoint FromModel(SavedPoint m, int saveId) => new()
        {
            SaveId = saveId, Bssid = m.Bssid, Ssid = m.Ssid, Authentication = m.Authentication,
            Encryption = m.Encryption, RadioType = m.RadioType, Manufacturer = m.Manufacturer,
            Channel = m.Channel, SecurityType = m.SecurityType, Signal = m.Signal, Rssi = m.Rssi,
            Latitude = m.Latitude, Longitude = m.Longitude, LastSeen = m.LastSeen.Ticks,
        };

        public SavedPoint ToModel() => new()
        {
            SaveId = SaveId, Bssid = Bssid, Ssid = Ssid, Authentication = Authentication,
            Encryption = Encryption, RadioType = RadioType, Manufacturer = Manufacturer,
            Channel = Channel, SecurityType = SecurityType, Signal = Signal, Rssi = Rssi,
            Latitude = Latitude, Longitude = Longitude, LastSeen = new DateTime(LastSeen, DateTimeKind.Utc),
        };
    }
}
