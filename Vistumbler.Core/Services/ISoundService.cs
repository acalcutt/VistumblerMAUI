namespace Vistumbler.Core.Services;

/// <summary>
/// Plays notification sounds (new AP discovered, specific SSID matched, etc.)
/// Uses Plugin.Maui.Audio cross-platform.
/// </summary>
public interface ISoundService
{
    bool SoundEnabled { get; set; }
    Task PlayNewNetworkAsync();
    Task PlayConnectedNetworkAsync();
}
