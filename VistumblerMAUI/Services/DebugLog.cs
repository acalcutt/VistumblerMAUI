namespace VistumblerMAUI.Services;

/// <summary>
/// Opt-in diagnostic logging, toggled from Settings → Advanced → Debug logging
/// (off by default). Gates the chatty runtime diagnostics (per-fix GPS lines,
/// AP-layer refresh summaries) that are invaluable when debugging in the field
/// via `adb logcat` but pure noise otherwise. Persisted across sessions.
/// </summary>
public static class DebugLog
{
    private const string Key = "debug_logging";
    private static bool _enabled = Preferences.Get(Key, false);

    public static bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Preferences.Set(Key, value); }
    }

    public static void Write(string message)
    {
        if (_enabled) System.Diagnostics.Debug.WriteLine(message);
    }
}
