using System.Text.Json;

namespace VistumblerMAUI.Services;

/// <summary>A saved file waiting to be uploaded to WifiDB.</summary>
public sealed class PendingWifiDbUpload
{
    public string   Path      { get; set; } = string.Empty;
    public string   Title     { get; set; } = string.Empty;
    public string   Notes     { get; set; } = string.Empty;
    public DateTime Added     { get; set; }
    public string?  LastError { get; set; }
}

/// <summary>
/// Files Save &amp; Clear saved that still have to go to WifiDB ("Upload each save to WifiDB"). A save is queued
/// and the queue is worked through right away; whatever can't be uploaded (no connection, WifiDB down, no account
/// set) stays queued, across restarts, and is retried when the connection comes back, when the app starts, after
/// the next save, or from Settings → Save &amp; Clear → Retry now.
/// </summary>
public sealed class WifiDbUploadQueue
{
    private const string QueueKey = "WifiDb_UploadQueue";

    // Short enough that a bad connection fails and stays queued instead of holding up the next save
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(2);

    private readonly object _lock = new();
    private readonly SemaphoreSlim _processing = new(1, 1);
    private readonly List<PendingWifiDbUpload> _items;
    private bool _started;

    /// <summary>Raised (on any thread) when files are added, uploaded or dropped, or an attempt fails.</summary>
    public event EventHandler? Changed;

    public WifiDbUploadQueue()
    {
        try
        {
            _items = JsonSerializer.Deserialize<List<PendingWifiDbUpload>>(Preferences.Get(QueueKey, "[]")) ?? new();
        }
        catch
        {
            _items = new();
        }
    }

    public int Count { get { lock (_lock) return _items.Count; } }

    /// <summary>Why the last attempt failed for the oldest file still waiting, or null.</summary>
    public string? LastError { get { lock (_lock) return _items.FirstOrDefault()?.LastError; } }

    public bool Contains(string path)
    {
        lock (_lock) return _items.Any(i => i.Path == path);
    }

    /// <summary>Why <paramref name="path"/> hasn't been uploaded yet, or null when it isn't waiting.</summary>
    public string? ErrorFor(string path)
    {
        lock (_lock) return _items.FirstOrDefault(i => i.Path == path)?.LastError;
    }

    /// <summary>Starts retrying when the device comes back online, and tries once now. Call once at startup.</summary>
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

    public void Enqueue(string path, string title, string notes)
    {
        lock (_lock)
        {
            if (_items.Any(i => i.Path == path)) return;
            _items.Add(new PendingWifiDbUpload { Path = path, Title = title, Notes = notes, Added = DateTime.UtcNow });
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets every waiting upload. The saved files themselves are kept.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Uploads the waiting files, oldest first, and returns how many went. Stops at the first connection
    /// failure, since the rest would fail the same way. Does nothing while another run is going, without an
    /// account, or while the device is offline. With <paramref name="waitForRunning"/>, waits for a run that is
    /// already going and then runs again, for a caller that needs to know how its own file got on.
    /// </summary>
    public async Task<int> ProcessAsync(bool waitForRunning = false)
    {
        if (Count == 0) return 0;
        if (waitForRunning) await _processing.WaitAsync();
        else if (!await _processing.WaitAsync(0)) return 0;
        int uploaded = 0;
        try
        {
            if (string.IsNullOrWhiteSpace(WifiDbSettings.User))
            {
                SetError("waiting for a WifiDB account (Settings → WifiDB)");
                return 0;
            }
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                SetError("waiting for an internet connection");
                return 0;
            }

            PendingWifiDbUpload? item;
            while ((item = Peek()) is not null)
            {
                if (!SaveFolder.Exists(item.Path))
                {
                    Remove(item);   // deleted or moved; nothing left to send
                    continue;
                }

                // A file in a folder picked on Android is a content URI; upload a local copy of it
                WifiDbUploadResult result;
                string? copy = null;
                try
                {
                    var (local, isCopy) = await SaveFolder.GetLocalFileAsync(item.Path);
                    if (isCopy) copy = local;
                    using var timeout = new CancellationTokenSource(UploadTimeout);
                    result = await WifiDbUploader.UploadAsync(local, WifiDbSettings.User, WifiDbSettings.ApiKey,
                        otherUsers: "", item.Title, item.Notes, timeout.Token);
                }
                catch (Exception ex)
                {
                    result = new WifiDbUploadResult(false, $"Couldn't read the saved file: {ex.Message}");
                }
                finally
                {
                    if (copy is not null) try { File.Delete(copy); } catch { /* cache */ }
                }

                // Already there: an earlier attempt got through even though its answer was lost
                if (result.Success || result.AlreadyKnown)
                {
                    DebugLog.Write($"[UploadQueue] {Path.GetFileName(item.Path)}: {result.Message}");
                    Remove(item);
                    uploaded++;
                    // WifiDB keeps its own copy of every upload, so the local one can go when the user wants
                    if (SaveAndClearSettings.DeleteAfterUpload)
                    {
                        try { SaveFolder.Delete(item.Path); }
                        catch (Exception ex) { DebugLog.Write($"[UploadQueue] couldn't delete {item.Path}: {ex.Message}"); }
                    }
                    continue;
                }

                lock (_lock)
                {
                    item.LastError = result.Message;
                    Save();
                }
                Changed?.Invoke(this, EventArgs.Empty);
                // The connection or server is the problem: the rest would fail the same way. A file WifiDB refused
                // stays queued with its reason (shown in Settings) but doesn't hold up the ones after it.
                if (result.ConnectionFailed) break;
                if (!MoveToBack(item)) break;
            }
        }
        finally
        {
            lock (_lock) _skipped.Clear();
            _processing.Release();
        }
        return uploaded;
    }

    private PendingWifiDbUpload? Peek()
    {
        lock (_lock) return _items.FirstOrDefault(i => !_skipped.Contains(i));
    }

    // Files WifiDB refused in this run, so it moves on to the next one
    private readonly HashSet<PendingWifiDbUpload> _skipped = new();

    private bool MoveToBack(PendingWifiDbUpload item)
    {
        lock (_lock)
        {
            _skipped.Add(item);
            return !_items.All(_skipped.Contains);   // false once every file was refused this run
        }
    }

    private void Remove(PendingWifiDbUpload item)
    {
        lock (_lock)
        {
            _items.Remove(item);
            _skipped.Remove(item);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetError(string message)
    {
        lock (_lock)
        {
            foreach (var i in _items) i.LastError = message;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Callers hold _lock
    private void Save() => Preferences.Set(QueueKey, JsonSerializer.Serialize(_items));
}
