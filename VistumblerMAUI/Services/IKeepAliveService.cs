namespace VistumblerMAUI.Services;

/// <summary>
/// Keeps scanning and GPS alive while the app is not on screen. On Android this runs
/// a foreground service (persistent notification + partial wakelock) — the same
/// mechanism WiGLE / vistumbler-android use to keep collecting with the screen off;
/// without it, Android suspends Wi-Fi scans and location callbacks shortly after the
/// screen turns off. Windows/iOS use the no-op implementation.
/// </summary>
public interface IKeepAliveService
{
    /// <summary>Idempotent — safe to call when already running.</summary>
    void Start();

    void Stop();
}

public sealed class NullKeepAliveService : IKeepAliveService
{
    public void Start() { }
    public void Stop() { }
}
