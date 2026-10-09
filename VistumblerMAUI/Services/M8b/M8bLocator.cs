using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services.M8b;

/// <summary>
/// Offline "where am I" from Wi-Fi: the m8b files imported into the app (built here from earlier sessions, by
/// WiGLE WiFi Wardriving, or by a WifiDB server), plus, optionally, the APs of the session open now, looked up
/// with the BSSIDs currently in range. Needs no GPS and no network, so it works indoors and underground.
/// </summary>
public static class M8bLocator
{
    private const string UseSessionKey = "M8b_UseSession";

    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "m8b");

    /// <summary>Whether the open session's APs are searched too, as well as the imported files.</summary>
    public static bool UseSession
    {
        get => Preferences.Get(UseSessionKey, true);
        set => Preferences.Set(UseSessionKey, value);
    }

    private static readonly object Gate = new();
    private static Dictionary<string, M8bFile>? _files;   // by path
    private static (int Count, DateTime Built, M8bFile File)? _session;

    /// <summary>The imported files, with how many records each has. Loads them the first time.</summary>
    public static IReadOnlyList<(string Name, int Records)> Files()
    {
        var files = Loaded();
        lock (Gate)
            return files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(f => (Path.GetFileName(f.Key), f.Value.RecordCount)).ToList();
    }

    private static Dictionary<string, M8bFile> Loaded()
    {
        lock (Gate)
        {
            if (_files is not null) return _files;
            _files = new Dictionary<string, M8bFile>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(Folder)) return _files;
            foreach (var path in Directory.EnumerateFiles(Folder, "*.m8b"))
            {
                try { _files[path] = M8bFile.Read(path); }
                catch (Exception ex) { DebugLog.Write($"[m8b] couldn't load {path}: {ex.Message}"); }
            }
            return _files;
        }
    }

    /// <summary>Copies an m8b file into the app and loads it. Throws if it isn't one.</summary>
    public static async Task<int> ImportAsync(Stream source, string fileName)
    {
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        buffer.Position = 0;
        var file = await Task.Run(() => M8bFile.Read(buffer));   // check it before keeping it

        Directory.CreateDirectory(Folder);
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name)) name = "imported";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var path = Path.Combine(Folder, name + ".m8b");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, $"{name} ({i}).m8b");
        await File.WriteAllBytesAsync(path, buffer.ToArray());

        var files = Loaded();
        lock (Gate) files[path] = file;
        return file.RecordCount;
    }

    public static void Remove(string name)
    {
        var path = Path.Combine(Folder, Path.GetFileName(name));
        var files = Loaded();
        lock (Gate) files.Remove(path);
        try { File.Delete(path); } catch (IOException) { }
    }

    /// <summary>
    /// The squares the visible BSSIDs place you in, most agreed first; empty if nothing in range is known.
    /// <paramref name="sessionAps"/> are searched too when <see cref="UseSession"/> is on.
    /// </summary>
    public static IReadOnlyList<M8bMatch> Locate(IEnumerable<string> visibleBssids, IReadOnlyCollection<AccessPoint>? sessionAps)
    {
        var sources = Loaded().Values.ToList();
        if (UseSession && sessionAps is not null && SessionIndex(sessionAps) is { } session) sources.Add(session);
        return sources.Count == 0 ? Array.Empty<M8bMatch>() : M8bFile.Locate(sources, visibleBssids);
    }

    /// <summary>Whether there is anything to search at all.</summary>
    public static bool HasSources(IReadOnlyCollection<AccessPoint>? sessionAps) =>
        Loaded().Count > 0 || (UseSession && sessionAps is not null && sessionAps.Any(a => a.HasGps));

    // The open session as an m8b index, rebuilt when it has grown and at most every 15 s
    private static M8bFile? SessionIndex(IReadOnlyCollection<AccessPoint> aps)
    {
        lock (Gate)
        {
            if (_session is { } s && (s.Count == aps.Count || DateTime.UtcNow - s.Built < TimeSpan.FromSeconds(15)))
                return s.File;
        }
        var points = aps.Where(a => a.HasGps).Select(a => (a.Bssid, a.Latitude!.Value, a.Longitude!.Value)).ToList();
        var buffer = new MemoryStream();
        M8bFile.Write(buffer, points);
        buffer.Position = 0;
        var file = M8bFile.Read(buffer);
        lock (Gate) _session = (aps.Count, DateTime.UtcNow, file);
        return file;
    }
}
