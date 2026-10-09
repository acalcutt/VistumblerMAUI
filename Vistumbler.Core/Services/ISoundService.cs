namespace Vistumbler.Core.Services;

/// <summary>
/// Plays the original Vistumbler's sounds: the new-AP sound, the error sound, and speaking an AP's signal.
/// </summary>
public interface ISoundService
{
    /// <summary>Whether the new-AP sound plays (Settings → Sound).</summary>
    bool SoundEnabled { get; set; }

    /// <summary>
    /// Plays the new-AP sound for one scan cycle's newly found APs, given their signals (0-100): once for the
    /// cycle, or once per AP, optionally at a volume that follows its signal, depending on the settings.
    /// </summary>
    Task PlayNewNetworksAsync(IReadOnlyList<int> signals);

    /// <summary>Plays the error sound, e.g. when a GPS receiver stops sending data.</summary>
    Task PlayErrorAsync();

    /// <summary>Speaks a signal value (0-100), with text to speech or the original's recorded words.</summary>
    Task SpeakSignalAsync(int signal);
}
