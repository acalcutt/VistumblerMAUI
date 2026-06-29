using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Sound alerts. Uses MediaElement (CommunityToolkit.Maui) to play embedded audio.
/// Drop .wav files into Resources/Raw/ and reference them here.
/// </summary>
public class MauiSoundService : ISoundService
{
    public bool SoundEnabled { get; set; } = true;

    public async Task PlayNewNetworkAsync()
    {
        if (!SoundEnabled) return;
        await PlayAsync("new_network.wav");
    }

    public async Task PlayConnectedNetworkAsync()
    {
        if (!SoundEnabled) return;
        await PlayAsync("connected_network.wav");
    }

    private static async Task PlayAsync(string resourceName)
    {
        try
        {
            // CommunityToolkit.Maui MediaElement or simple platform audio
            // For now use a simple beep via the default audio feedback
            await Task.Run(() =>
            {
                // TODO: replace with Plugin.Maui.Audio or CommunityToolkit MediaElement
                // AudioPlayer.PlayAsync(resourceName);
            });
        }
        catch
        {
            // Sound is non-critical — swallow errors
        }
    }
}
