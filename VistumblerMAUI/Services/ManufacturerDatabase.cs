using System.IO.Compression;
using System.Text;

namespace VistumblerMAUI.Services;

/// <summary>
/// Looks up an AP's manufacturer from the first three bytes of its BSSID (the IEEE OUI), like the original
/// Vistumbler's Manufacturers.mdb (built by macmanuf) and VistumblerCS's Update Manufacturers. The list is a
/// gzipped "AABBCC&lt;tab&gt;Name" file: a copy ships with the app (Resources/Raw/manufacturers.txt.gz), so names
/// show from the first scan even offline, and <see cref="UpdateAsync"/> replaces it with IEEE's current list.
/// About 40,000 entries, held in memory.
/// </summary>
public sealed class ManufacturerDatabase
{
    private const string BundledFile = "manufacturers.txt.gz";
    public const string IeeeUrl = "https://standards-oui.ieee.org/oui/oui.txt";

    private static string UpdatedFile => Path.Combine(FileSystem.AppDataDirectory, "manufacturers.txt.gz");

    private volatile Dictionary<string, string> _byPrefix = new();
    private Task? _loading;

    /// <summary>Raised (on any thread) when the list is loaded or updated, so names can be filled in.</summary>
    public event EventHandler? Changed;

    public int Count => _byPrefix.Count;

    /// <summary>When the updated list was downloaded, or null while the copy that came with the app is in use.</summary>
    public DateTime? UpdatedOn => File.Exists(UpdatedFile) ? File.GetLastWriteTime(UpdatedFile) : null;

    /// <summary>Loads the list in the background, once; lookups return "" until it's loaded.</summary>
    public Task LoadAsync() => _loading ??= Task.Run(async () =>
    {
        try
        {
            await using var stream = File.Exists(UpdatedFile)
                ? File.OpenRead(UpdatedFile)
                : await FileSystem.OpenAppPackageFileAsync(BundledFile);
            _byPrefix = Read(stream);
            DebugLog.Write($"[Manufacturers] loaded {_byPrefix.Count}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Manufacturers] couldn't load the list: {ex.Message}");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>The manufacturer for a BSSID such as "AA:BB:CC:11:22:33", or "" when it isn't listed.</summary>
    public string Lookup(string? bssid)
    {
        if (string.IsNullOrEmpty(bssid)) return string.Empty;
        Span<char> prefix = stackalloc char[6];
        int n = 0;
        foreach (char c in bssid)
        {
            if (!Uri.IsHexDigit(c)) continue;
            prefix[n++] = char.ToUpperInvariant(c);
            if (n == 6) break;
        }
        return n == 6 && _byPrefix.TryGetValue(new string(prefix), out var name) ? name : string.Empty;
    }

    /// <summary>Fills in the manufacturer of APs that have none, e.g. from a file that didn't record it.</summary>
    public void FillMissing(IEnumerable<Vistumbler.Core.Models.AccessPoint> aps)
    {
        foreach (var ap in aps)
            if (string.IsNullOrEmpty(ap.Manufacturer) && Lookup(ap.Bssid) is { Length: > 0 } name)
                ap.Manufacturer = name;
    }

    /// <summary>The app's instance, for code outside dependency injection.</summary>
    public static ManufacturerDatabase? Current =>
        IPlatformApplication.Current?.Services.GetService(typeof(ManufacturerDatabase)) as ManufacturerDatabase;

    /// <summary>
    /// Downloads IEEE's current OUI list and makes it the one in use (kept across restarts). Returns how many
    /// manufacturers it has. Throws when the download fails, leaving the current list in place.
    /// </summary>
    public async Task<int> UpdateAsync(HttpClient http, CancellationToken ct = default)
    {
        var text = await http.GetStringAsync(IeeeUrl, ct);
        var entries = ParseIeee(text);
        if (entries.Count < 1000)
            throw new InvalidDataException($"IEEE's list had only {entries.Count} entries; keeping the current one.");

        var temp = UpdatedFile + ".tmp";
        await using (var file = File.Create(temp))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            foreach (var (prefix, name) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                await writer.WriteAsync($"{prefix}\t{name}\n");
        }
        File.Move(temp, UpdatedFile, overwrite: true);

        _byPrefix = entries;
        Changed?.Invoke(this, EventArgs.Empty);
        return entries.Count;
    }

    // IEEE's oui.txt: "286FB9     (base 16)\t\tNokia Shanghai Bell Co., Ltd."
    private static Dictionary<string, string> ParseIeee(string text)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            int marker = rawLine.IndexOf("(base 16)", StringComparison.Ordinal);
            if (marker < 0) continue;
            var prefix = rawLine[..marker].Trim().ToUpperInvariant();
            var name = rawLine[(marker + 9)..].Trim();
            if (prefix.Length == 6 && name.Length > 0) entries[prefix] = name;
        }
        return entries;
    }

    private static Dictionary<string, string> Read(Stream compressed)
    {
        var entries = new Dictionary<string, string>(45_000, StringComparer.Ordinal);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            int tab = line.IndexOf('\t');
            if (tab == 6) entries[line[..6]] = line[(tab + 1)..];
        }
        return entries;
    }
}
