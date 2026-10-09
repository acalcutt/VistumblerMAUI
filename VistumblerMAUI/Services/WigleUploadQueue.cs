namespace VistumblerMAUI.Services;

/// <summary>
/// Saves waiting to go to WiGLE (Settings → WiGLE → Upload each Save &amp; Clear). Each is a gzipped WiGLE CSV in the
/// app's own folder, written before Save &amp; Clear empties the session, and deleted once WiGLE takes it. Kept apart
/// from the WifiDB queue so neither holds up the other; retried when the connection comes back, at startup and after
/// the next save.
/// </summary>
public sealed class WigleUploadQueue
{
    private const string ErrorKey = "Wigle_LastError";
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _processing = new(1, 1);
    private bool _started;

    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "wigle-uploads");

    /// <summary>Raised (on any thread) when a file is added or uploaded, or an attempt fails.</summary>
    public event EventHandler? Changed;

    public int Count => Pending().Count;

    /// <summary>Why the last attempt failed, or null.</summary>
    public string? LastError
    {
        get { var e = Preferences.Get(ErrorKey, string.Empty); return e.Length == 0 ? null : e; }
        private set => Preferences.Set(ErrorKey, value ?? string.Empty);
    }

    private static List<string> Pending() => Directory.Exists(Folder)
        ? Directory.GetFiles(Folder, "*.csv.gz").OrderBy(p => p, StringComparer.Ordinal).ToList()
        : new List<string>();

    /// <summary>A path for a new file in the queue, named as WiGLE's client names its uploads.</summary>
    public static string NewPath()
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, $"WigleWifi_{DateTime.Now:yyyyMMddHHmmss}.csv.gz");
    }

    public void Added() => Changed?.Invoke(this, EventArgs.Empty);

    public void Start()
    {
        if (_started) return;
        _started = true;
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet) _ = ProcessAsync();
        };
        _ = ProcessAsync();
    }

    /// <summary>Forgets every waiting upload, deleting the files (they're copies; the saved VS1 files are kept).</summary>
    public void Clear()
    {
        foreach (var p in Pending()) try { File.Delete(p); } catch (IOException) { }
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Uploads the waiting files, oldest first, and returns how many went. Stops at the first connection failure.
    /// Does nothing while WiGLE uploads are off or have no account, or the device is offline.
    /// </summary>
    public async Task<int> ProcessAsync(bool waitForRunning = false)
    {
        if (Count == 0) return 0;
        if (waitForRunning) await _processing.WaitAsync();
        else if (!await _processing.WaitAsync(0)) return 0;
        int uploaded = 0;
        try
        {
            if (!WigleSettings.Ready)
            {
                LastError = "waiting for a WiGLE API name and token (Settings → WiGLE)";
                return 0;
            }
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                LastError = "waiting for an internet connection";
                return 0;
            }

            foreach (var path in Pending())
            {
                using var timeout = new CancellationTokenSource(UploadTimeout);
                var result = await WigleUploader.UploadAsync(path, timeout.Token);
                DebugLog.Write($"[WigleQueue] {Path.GetFileName(path)}: {result.Message}");
                if (result.Success)
                {
                    try { File.Delete(path); } catch (IOException) { }
                    uploaded++;
                    LastError = null;
                    Changed?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                LastError = result.Message;
                Changed?.Invoke(this, EventArgs.Empty);
                if (result.ConnectionFailed || !WigleSettings.HasAccount) break;   // the rest would fail the same way
            }
        }
        finally
        {
            _processing.Release();
        }
        return uploaded;
    }
}
