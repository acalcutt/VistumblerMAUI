using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Persists access points, signal history, and GPS tracks in a local SQLite database.
/// Implementation uses sqlite-net-pcl (cross-platform).
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();

    // Access points
    Task<int> UpsertAccessPointAsync(AccessPoint ap);
    Task<AccessPoint?> GetAccessPointByBssidAsync(string bssid);
    Task<List<AccessPoint>> GetAllAccessPointsAsync();
    Task ClearAllAccessPointsAsync();

    // Signal history
    Task AddSignalHistoryAsync(SignalHistory entry);
    Task<List<SignalHistory>> GetSignalHistoryAsync(int apId);

    // GPS track
    Task<int> AddGpsDataAsync(GpsData gpsData);

    // Manufacturer OUI lookup
    Task<string> GetManufacturerAsync(string ouiPrefix);
    Task BulkUpsertManufacturersAsync(IEnumerable<(string OuiPrefix, string Manufacturer)> entries);

    // Labels (user-assigned friendly names per BSSID)
    Task<string?> GetLabelAsync(string bssid);
    Task SetLabelAsync(string bssid, string label);
}
