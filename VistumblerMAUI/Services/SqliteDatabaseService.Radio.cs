using SQLite;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

/// <summary>
/// Cell towers and Bluetooth devices: their own tables beside the APs, as WiGLE and WifiDB keep them. Readings
/// link to the same GpsData rows the APs' history does.
/// </summary>
public partial class SqliteDatabaseService
{
    public async Task SaveRadioCycleAsync(IReadOnlyList<RadioNetwork> networks, GpsData? gps, DateTime scanTime)
    {
        if (networks.Count == 0) return;
        await InitializeAsync();

        await _db!.RunInTransactionAsync(conn =>
        {
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

            foreach (var n in networks)
            {
                UpsertRadio(conn, n);
                conn.Insert(new DbRadioReading { NetworkId = n.Id, GpsId = gpsId, Rssi = n.Rssi, Timestamp = scanTime.Ticks });
            }
        });
    }

    public async Task ImportRadioNetworksAsync(IReadOnlyList<RadioNetwork> networks)
    {
        if (networks.Count == 0) return;
        await InitializeAsync();

        await _db!.RunInTransactionAsync(conn =>
        {
            foreach (var n in networks)
            {
                // Merge with what's stored: earliest first seen, latest last seen, strongest reading's position
                var existing = conn.Table<DbRadioNetwork>().FirstOrDefault(x => x.Key == n.Key);
                if (existing is not null)
                {
                    var old = existing.ToModel();
                    if (old.FirstSeen != default && (n.FirstSeen == default || old.FirstSeen < n.FirstSeen)) n.FirstSeen = old.FirstSeen;
                    if (old.LastSeen > n.LastSeen) n.LastSeen = old.LastSeen;
                    if (old.HighestRssi >= n.HighestRssi)
                    {
                        n.HighestRssi = old.HighestRssi;
                        n.Latitude = old.Latitude;
                        n.Longitude = old.Longitude;
                    }
                    if (string.IsNullOrEmpty(n.Name)) n.Name = old.Name;
                }
                UpsertRadio(conn, n);

                foreach (var r in n.History.OrderBy(r => r.Timestamp))
                {
                    int gpsId = 0;
                    if (r.Latitude is { } lat && r.Longitude is { } lon)
                    {
                        var g = new DbGpsData { Latitude = lat, Longitude = lon, Altitude = r.Altitude, Timestamp = r.Timestamp.Ticks, Quality = 1 };
                        conn.Insert(g);
                        gpsId = g.Id;
                    }
                    conn.Insert(new DbRadioReading { NetworkId = n.Id, GpsId = gpsId, Rssi = r.Rssi, Timestamp = r.Timestamp.Ticks });
                }
            }
        });
    }

    // Insert or update a network's row by its key, setting its Id
    private static void UpsertRadio(SQLiteConnection conn, RadioNetwork n)
    {
        var row = DbRadioNetwork.FromModel(n);
        var existing = conn.Table<DbRadioNetwork>().FirstOrDefault(x => x.Key == n.Key);
        if (existing is null) { conn.Insert(row); n.Id = row.Id; }
        else { row.Id = n.Id = existing.Id; conn.Update(row); }
    }

    public async Task<List<RadioNetwork>> GetAllRadioNetworksAsync()
    {
        await InitializeAsync();
        var rows = await _db!.Table<DbRadioNetwork>().ToListAsync();
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<List<RadioReading>> GetRadioHistoryAsync(int networkId)
    {
        await InitializeAsync();
        var rows = await _db!.QueryAsync<RadioReadingRow>(
            @"SELECT r.Id, r.NetworkId, r.GpsId, r.Rssi, r.Timestamp,
                     g.Latitude AS Latitude, g.Longitude AS Longitude, g.Altitude AS Altitude
              FROM RadioReadings r
              LEFT JOIN GpsData g ON g.Id = r.GpsId
              WHERE r.NetworkId = ? ORDER BY r.Timestamp ASC, r.Id ASC", networkId);
        return rows.Select(r => new RadioReading
        {
            Id        = r.Id,
            NetworkId = r.NetworkId,
            GpsId     = r.GpsId,
            Rssi      = r.Rssi,
            Latitude  = r.Latitude,
            Longitude = r.Longitude,
            Altitude  = r.Altitude,
            Timestamp = new DateTime(r.Timestamp, DateTimeKind.Utc),
        }).ToList();
    }

    [Table("RadioNetworks")]
    private class DbRadioNetwork
    {
        [PrimaryKey, AutoIncrement] public int     Id           { get; set; }
        [Indexed(Unique = true)]    public string  Key          { get; set; } = string.Empty;
                                    public int     Type         { get; set; }
                                    public string  Name         { get; set; } = string.Empty;
                                    public string  Capabilities { get; set; } = string.Empty;
                                    public string  Manufacturer { get; set; } = string.Empty;
                                    public int?    Channel      { get; set; }
                                    public int     Frequency    { get; set; }
                                    public int?    MfgrId       { get; set; }
                                    public int     Rssi         { get; set; }
                                    public int     HighestRssi  { get; set; }
                                    public long    FirstSeen    { get; set; }
                                    public long    LastSeen     { get; set; }
                                    public double? Latitude     { get; set; }
                                    public double? Longitude    { get; set; }

        public static DbRadioNetwork FromModel(RadioNetwork m) => new()
        {
            Id           = m.Id,
            Key          = m.Key,
            Type         = (int)m.Type,
            Name         = m.Name,
            Capabilities = m.Capabilities,
            Manufacturer = m.Manufacturer,
            Channel      = m.Channel,
            Frequency    = m.Frequency,
            MfgrId       = m.MfgrId,
            Rssi         = m.Rssi,
            HighestRssi  = m.HighestRssi,
            FirstSeen    = m.FirstSeen.Ticks,
            LastSeen     = m.LastSeen.Ticks,
            Latitude     = m.Latitude,
            Longitude    = m.Longitude,
        };

        public RadioNetwork ToModel() => new()
        {
            Id           = Id,
            Key          = Key,
            Type         = (RadioNetworkType)Type,
            Name         = Name,
            Capabilities = Capabilities,
            Manufacturer = Manufacturer,
            Channel      = Channel,
            Frequency    = Frequency,
            MfgrId       = MfgrId,
            Rssi         = Rssi,
            HighestRssi  = HighestRssi,
            FirstSeen    = FirstSeen == 0 ? default : new DateTime(FirstSeen, DateTimeKind.Utc),
            LastSeen     = LastSeen  == 0 ? default : new DateTime(LastSeen,  DateTimeKind.Utc),
            Latitude     = Latitude,
            Longitude    = Longitude,
        };
    }

    [Table("RadioReadings")]
    private class DbRadioReading
    {
        [PrimaryKey, AutoIncrement] public int  Id        { get; set; }
        [Indexed]                   public int  NetworkId { get; set; }
                                    public int  GpsId     { get; set; }
                                    public int  Rssi      { get; set; }
                                    public long Timestamp { get; set; }
    }

    private class RadioReadingRow
    {
        public int     Id        { get; set; }
        public int     NetworkId { get; set; }
        public int     GpsId     { get; set; }
        public int     Rssi      { get; set; }
        public double? Latitude  { get; set; }
        public double? Longitude { get; set; }
        public double? Altitude  { get; set; }
        public long    Timestamp { get; set; }
    }
}
