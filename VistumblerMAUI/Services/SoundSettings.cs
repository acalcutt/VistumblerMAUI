namespace VistumblerMAUI.Services;

/// <summary>When the new-AP sound plays, after the original Vistumbler's Sound settings.</summary>
public enum NewApSoundMode
{
    OncePerScan,        // the original's default: one sound for each scan that finds any new APs
    OncePerAp,          // one sound for each new AP
    OncePerApBySignal,  // one sound for each new AP, louder for a stronger signal
}

/// <summary>How a signal is spoken, after the original's SpeakType (2, text to speech, was its default).</summary>
public enum SpeakVoice
{
    TextToSpeech,       // the device's voice (the original used Windows SAPI)
    VistumblerSounds,   // the original's recorded words (Sounds/one.wav … percent.wav)
    Tone,               // a beep whose pitch follows the signal, standing in for the original's MIDI notes
}

/// <summary>What Speak signal says.</summary>
public enum SpeakValue
{
    SignalPercent,      // "65 percent"
    Rssi,               // "minus 65 dBm"
}

/// <summary>Sound settings (Settings → Sound), backed by MAUI <see cref="Preferences"/>.</summary>
public static class SoundSettings
{
    private const string NewApKey      = "Sound_NewAp";
    private const string ModeKey       = "Sound_NewApMode";
    private const string ErrorKey      = "Sound_Error";
    private const string SpeakKey      = "Sound_SpeakSignal";
    private const string VoiceKey      = "Sound_SpeakVoice";
    private const string PercentKey    = "Sound_SpeakPercent";
    private const string ValueKey      = "Sound_SpeakValue";
    private const string IntervalKey   = "Sound_SpeakIntervalMs";

    /// <summary>Play a sound when new APs are found (the original's default is on).</summary>
    public static bool NewApSound
    {
        get => Preferences.Get(NewApKey, true);
        set => Preferences.Set(NewApKey, value);
    }

    public static NewApSoundMode NewApMode
    {
        get => (NewApSoundMode)Preferences.Get(ModeKey, (int)NewApSoundMode.OncePerScan);
        set => Preferences.Set(ModeKey, (int)value);
    }

    /// <summary>Play the error sound when an external GPS receiver stops sending data.</summary>
    public static bool ErrorSound
    {
        get => Preferences.Get(ErrorKey, true);
        set => Preferences.Set(ErrorKey, value);
    }

    /// <summary>Speak the signal of the AP open in AP details while scanning.</summary>
    public static bool SpeakSignal
    {
        get => Preferences.Get(SpeakKey, false);
        set => Preferences.Set(SpeakKey, value);
    }

    public static SpeakVoice Voice
    {
        get => (SpeakVoice)Preferences.Get(VoiceKey, (int)SpeakVoice.TextToSpeech);
        set => Preferences.Set(VoiceKey, (int)value);
    }

    /// <summary>Speak the signal in percent (the original) or the RSSI in dBm.</summary>
    public static SpeakValue Value
    {
        get => (SpeakValue)Preferences.Get(ValueKey, (int)SpeakValue.SignalPercent);
        set => Preferences.Set(ValueKey, (int)value);
    }

    /// <summary>Say "percent" after the number.</summary>
    public static bool SayPercent
    {
        get => Preferences.Get(PercentKey, true);
        set => Preferences.Set(PercentKey, value);
    }

    /// <summary>How often to speak the signal, in milliseconds (the original's default is 2000).</summary>
    public static int SpeakIntervalMs
    {
        get => Preferences.Get(IntervalKey, 2000);
        set => Preferences.Set(IntervalKey, Math.Clamp(value, 1000, 60_000));
    }
}
