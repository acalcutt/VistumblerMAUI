using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Writes the current session to a Vistumbler VS1/VSZ file with every AP's signal and GPS history, the
/// file Save &amp; Clear keeps and the one uploaded to WifiDB.
/// </summary>
public static class SessionFileExporter
{
    /// <summary>Exports to <paramref name="path"/> and returns the number of APs written; 0 writes nothing.</summary>
    public static async Task<int> ExportAsync(IDatabaseService db, IExportService export, string path, SaveFileFormat format)
    {
        await db.InitializeAsync();
        var aps = await db.GetAllAccessPointsAsync();
        if (aps.Count == 0) return 0;
        ManufacturerDatabase.Current?.FillMissing(aps);   // APs saved before the lookup existed have none

        foreach (var ap in aps)
            ap.SignalHistory = await db.GetSignalHistoryAsync(ap.ApId);
        var gpsFixes = await db.GetAllGpsAsync();
        var radios = await LoadRadiosAsync(db);   // cell towers and Bluetooth, VS1 4.1's third section

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (format == SaveFileFormat.Vsz)
            await export.ExportToVszAsync(path, aps, gpsFixes, radios);
        else
            await export.ExportToVs1Async(path, aps, gpsFixes, radios);
        return aps.Count;
    }

    /// <summary>The session's cell towers and Bluetooth devices, each with its readings.</summary>
    public static async Task<List<Vistumbler.Core.Models.RadioNetwork>> LoadRadiosAsync(IDatabaseService db)
    {
        var radios = await db.GetAllRadioNetworksAsync();
        foreach (var n in radios) n.History = await db.GetRadioHistoryAsync(n.Id);
        return radios;
    }
}
