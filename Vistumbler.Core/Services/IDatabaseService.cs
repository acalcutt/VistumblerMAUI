using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Persists access points, signal history, and GPS tracks in a local SQLite database.
/// Implementation uses sqlite-net-pcl (cross-platform).
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();

    /// <summary>Close the current database connection (e.g. before switching/deleting the session file).</summary>
    Task CloseAsync();

    // Access points
    Task<int> UpsertAccessPointAsync(AccessPoint ap);
    /// <summary>
    /// Import access points into the AP/HIST/GPS tables: upserts each AP, writes its
    /// SignalHistory samples as HIST rows (creating GPS rows for samples with a position),
    /// and (re)computes the AP's first/last/high history links. Runs in one transaction.
    /// </summary>
    Task ImportAccessPointsAsync(IReadOnlyList<AccessPoint> aps);
    /// <summary>Update only the first/last/high history-link ids for an AP (fast path used by the scan loop).</summary>
    Task UpdateApHistLinksAsync(AccessPoint ap);
    /// <summary>
    /// Persist one scan cycle in a single transaction: writes one GPS row for the fix (if any),
    /// upserts each AP, appends a HIST sample linked to that GPS, and maintains each AP's
    /// first/last/high history links. Much faster than per-AP round-trips for large cycles.
    /// </summary>
    Task SaveScanCycleAsync(IReadOnlyList<AccessPoint> aps, GpsData? gps, DateTime scanTime);
    Task<AccessPoint?> GetAccessPointByBssidAsync(string bssid);
    Task<List<AccessPoint>> GetAllAccessPointsAsync();
    Task ClearAllAccessPointsAsync();

    // Signal history
    /// <summary>Insert a signal-history sample; returns its new row id (for AP hist links).</summary>
    Task<int> AddSignalHistoryAsync(SignalHistory entry);
    Task<List<SignalHistory>> GetSignalHistoryAsync(int apId);

    // GPS track
    Task<int> AddGpsDataAsync(GpsData gpsData);
    /// <summary>All GPS fixes recorded this session (used by VS1/VSZ export).</summary>
    Task<List<GpsData>> GetAllGpsAsync();

    // Manufacturer OUI lookup
    Task<string> GetManufacturerAsync(string ouiPrefix);
    Task BulkUpsertManufacturersAsync(IEnumerable<(string OuiPrefix, string Manufacturer)> entries);

    // Labels (user-assigned friendly names per BSSID)
    Task<string?> GetLabelAsync(string bssid);
    Task SetLabelAsync(string bssid, string label);
}
