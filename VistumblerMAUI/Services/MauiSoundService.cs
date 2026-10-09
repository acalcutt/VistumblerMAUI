using Plugin.Maui.Audio;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// The original Vistumbler's sounds, with its own recordings (Resources/Raw/Sounds, from Vistumbler/Sounds):
/// new_ap.wav when APs are found, error.wav when a GPS receiver goes quiet, and the signal of the AP open in
/// AP details spoken with the device's text to speech or the recorded words. Settings in <see cref="SoundSettings"/>.
/// </summary>
public class MauiSoundService : ISoundService
{
    // One sound sequence at a time: a busy scan shouldn't pile up minutes of beeps
    private readonly SemaphoreSlim _playing = new(1, 1);
    private const int MaxSoundsPerScan = 10;

    public bool SoundEnabled
    {
        get => SoundSettings.NewApSound;
        set => SoundSettings.NewApSound = value;
    }

    public async Task PlayNewNetworksAsync(IReadOnlyList<int> signals)
    {
        if (!SoundEnabled || signals.Count == 0 || !await _playing.WaitAsync(0)) return;
        try
        {
            switch (SoundSettings.NewApMode)
            {
                case NewApSoundMode.OncePerAp:
                    foreach (var _ in signals.Take(MaxSoundsPerScan)) await PlayAsync("new_ap.wav");
                    break;
                case NewApSoundMode.OncePerApBySignal:
                    foreach (var signal in signals.Take(MaxSoundsPerScan))
                        await PlayAsync("new_ap.wav", Math.Clamp(signal / 100.0, 0.1, 1.0));
                    break;
                default:
                    await PlayAsync("new_ap.wav");
                    break;
            }
        }
        finally
        {
            _playing.Release();
        }
    }

    public async Task PlayErrorAsync()
    {
        if (!SoundSettings.ErrorSound || !await _playing.WaitAsync(0)) return;
        try { await PlayAsync("error.wav"); }
        finally { _playing.Release(); }
    }

    public async Task SpeakSignalAsync(int signal, int? rssi)
    {
        signal = Math.Clamp(signal, 0, 100);
        if (!await _playing.WaitAsync(0)) return;   // skip a reading rather than fall behind
        try
        {
            // RSSI when chosen and known; out of range there is none, so fall back to the signal (0 %)
            bool sayRssi = SoundSettings.Value == SpeakValue.Rssi && rssi is not null && signal > 0;
            switch (SoundSettings.Voice)
            {
                case SpeakVoice.Tone:
                    await PlayToneAsync(signal);
                    break;
                case SpeakVoice.VistumblerSounds:
                    // The recordings only go up to "one hundred" and have no "minus", so RSSI is said as its size
                    int number = sayRssi ? Math.Min(100, Math.Abs(rssi!.Value)) : signal;
                    foreach (var word in Words(number)) await PlayAsync(word + ".wav");
                    if (!sayRssi && SoundSettings.SayPercent) await PlayAsync("percent.wav");
                    break;
                default:
                    var text = sayRssi
                        ? (SoundSettings.SayPercent ? $"{rssi} dBm" : $"{rssi}")
                        : (SoundSettings.SayPercent ? $"{signal} percent" : $"{signal}");
                    await TextToSpeech.Default.SpeakAsync(text);
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Sound] speaking failed: {ex.Message}");
        }
        finally
        {
            _playing.Release();
        }
    }

    // The original's recordings, spelled as their file names are ("fourty", "eightteen")
    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eightteen", "nineteen",
    };
    private static readonly string[] Tens = { "", "", "twenty", "thirty", "fourty", "fifty", "sixty", "seventy", "eighty", "ninety" };

    private static IEnumerable<string> Words(int n)
    {
        if (n == 100) return new[] { "one", "hundred" };
        if (n < 20) return new[] { Ones[n] };
        return n % 10 == 0 ? new[] { Tens[n / 10] } : new[] { Tens[n / 10], Ones[n % 10] };
    }

    /// <summary>
    /// A short beep whose pitch follows the signal, like the original's MIDI notes: from about 220 Hz at 0 %
    /// to 1760 Hz at 100 %, three octaves, so a stronger signal is plainly higher. Generated as a WAV in memory.
    /// </summary>
    private static async Task PlayToneAsync(int signal)
    {
        const int sampleRate = 22050, millis = 180;
        double frequency = 220 * Math.Pow(2, 3 * signal / 100.0);
        int samples = sampleRate * millis / 1000;
        using var wav = new MemoryStream();
        using (var w = new BinaryWriter(wav, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                // Short fade in and out so it doesn't click
                double envelope = Math.Min(1, Math.Min(i, samples - i) / (sampleRate * 0.01));
                w.Write((short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * envelope * 0.6 * short.MaxValue));
            }
        }
        wav.Position = 0;
        try
        {
            using var player = AudioManager.Current.CreatePlayer(wav);
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackEnded += (_, _) => ended.TrySetResult();
            player.Play();
            await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Sound] tone: {ex.Message}");
        }
    }

    /// <summary>Plays a sound from Resources/Raw/Sounds and waits for it to finish. Sound never stops the app.</summary>
    private static async Task PlayAsync(string file, double volume = 1.0)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("Sounds/" + file);
            using var player = AudioManager.Current.CreatePlayer(stream);
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackEnded += (_, _) => ended.TrySetResult();
            player.Volume = volume;
            player.Play();
            // A player that never reports the end mustn't hold up the next sound
            await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Sound] {file}: {ex.Message}");
        }
    }
}
