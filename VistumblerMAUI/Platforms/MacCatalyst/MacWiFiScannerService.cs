#if MACCATALYST
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Platforms.MacCatalyst;

/// <summary>
/// macOS WiFi scanner using CoreWLAN, the only macOS API that lists nearby networks. .NET has no CoreWLAN bindings for
/// Mac Catalyst (it is an AppKit-side framework), but a Catalyst app is a macOS process, so this loads the framework
/// at runtime and calls it through the Objective-C runtime.
/// Since macOS 14, CoreWLAN leaves out network names and BSSIDs unless the app has Location Services permission, so
/// scanning asks for it first and reports a scan error while it isn't granted.
/// </summary>
public sealed class MacWiFiScannerService : IWiFiScannerService
{
    const string CoreWlanPath = "/System/Library/Frameworks/CoreWLAN.framework/CoreWLAN";
    const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg, out IntPtr error);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendNint(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SendBool(IntPtr receiver, IntPtr selector, nint arg);

    static readonly Lazy<IntPtr> ClientClass = new(() =>
    {
        // Loading the framework registers its classes with the Objective-C runtime
        Dlfcn.dlopen(CoreWlanPath, 0);
        return Class.GetHandle("CWWiFiClient");
    });

    static IntPtr Sel(string name) => Selector.GetHandle(name);
    static string? Str(IntPtr nsString) => nsString == IntPtr.Zero ? null : Runtime.GetNSObject<NSString>(nsString)?.ToString();

    private string? _activeInterface;
    private bool _warnedAboutLocation;

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>?            ScanError;

    public bool IsScanning     { get; private set; }
    public int  ScanIntervalMs { get; set; } = 2000;

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        _warnedAboutLocation = false;
        try
        {
            await EnsureLocationPermissionAsync();
            while (!cancellationToken.IsCancellationRequested && IsScanning)
            {
                // A scan blocks for a few seconds, so keep it off the UI thread
                var aps = await Task.Run(ScanOnce, cancellationToken);
                if (aps.Count > 0)
                    AccessPointsDetected?.Invoke(this, new AccessPointsDetectedEventArgs { AccessPoints = aps });
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ScanError?.Invoke(this, new ScanErrorEventArgs { ErrorMessage = "macOS WiFi scan error", Exception = ex });
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void StopScanning() => IsScanning = false;

    static async Task EnsureLocationPermissionAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
                await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        });
    }

    /// <summary>The Wi-Fi interface to use: the chosen one, or the default (usually en0).</summary>
    IntPtr GetInterface()
    {
        var client = Send(ClientClass.Value, Sel("sharedWiFiClient"));
        if (client == IntPtr.Zero) throw new InvalidOperationException("CoreWLAN isn't available on this Mac.");
        if (_activeInterface is not null)
        {
            using var name = new NSString(_activeInterface);
            var named = Send(client, Sel("interfaceWithName:"), name.Handle);
            if (named != IntPtr.Zero) return named;
        }
        return Send(client, Sel("interface"));
    }

    List<AccessPoint> ScanOnce()
    {
        using var pool = new NSAutoreleasePool();
        var aps = new List<AccessPoint>();
        var iface = GetInterface();
        if (iface == IntPtr.Zero)
        {
            ScanError?.Invoke(this, new ScanErrorEventArgs { ErrorMessage = "No Wi-Fi interface found." });
            return aps;
        }

        var networks = Send(iface, Sel("scanForNetworksWithName:error:"), IntPtr.Zero, out var error);
        if (networks == IntPtr.Zero)
        {
            var message = error != IntPtr.Zero ? Runtime.GetNSObject<NSError>(error)?.LocalizedDescription : null;
            ScanError?.Invoke(this, new ScanErrorEventArgs { ErrorMessage = $"Wi-Fi scan failed: {message ?? "unknown error"}" });
            return aps;
        }

        var hidden = 0;
        foreach (var network in Runtime.GetNSObject<NSSet>(networks)?.ToArray<NSObject>() ?? [])
        {
            var bssid = NormalizeBssid(Str(Send(network.Handle, Sel("bssid"))));
            if (bssid is null) { hidden++; continue; }   // left out without Location Services permission

            var rssi = (int)SendNint(network.Handle, Sel("rssiValue"));
            var channelObj = Send(network.Handle, Sel("wlanChannel"));
            var channel = channelObj == IntPtr.Zero ? 0 : (int)SendNint(channelObj, Sel("channelNumber"));
            var band = channelObj == IntPtr.Zero ? 0 : (int)SendNint(channelObj, Sel("channelBand"));
            var (auth, encryption) = ReadSecurity(network.Handle);

            aps.Add(new AccessPoint
            {
                Bssid          = bssid,
                Ssid           = Str(Send(network.Handle, Sel("ssid"))) ?? string.Empty,
                Channel        = channel,
                FrequencyMhz   = ChannelToFrequency(channel, band),
                Rssi           = rssi,
                Signal         = RssiToPercent(rssi),
                RadioType      = ReadRadioType(network.Handle, band),
                NetworkType    = SendBool(network.Handle, Sel("ibss")) ? NetworkType.Adhoc : NetworkType.Infrastructure,
                Authentication = auth,
                Encryption     = encryption,
                IsActive       = true
            });
        }

        if (hidden > 0 && aps.Count == 0 && !_warnedAboutLocation)
        {
            _warnedAboutLocation = true;
            ScanError?.Invoke(this, new ScanErrorEventArgs
            {
                ErrorMessage = $"macOS found {hidden} networks but hides their names and BSSIDs until VistumblerMAUI may " +
                               "use your location: allow it in System Settings › Privacy & Security › Location Services."
            });
        }
        return aps;
    }

    // CWSecurity values (CoreWLAN/CoreWLANTypes.h)
    const nint SecurityNone = 0, SecurityWEP = 1, SecurityWPAPersonal = 2, SecurityWPAPersonalMixed = 3,
        SecurityWPA2Personal = 4, SecurityPersonal = 5, SecurityDynamicWEP = 6, SecurityWPAEnterprise = 7,
        SecurityWPAEnterpriseMixed = 8, SecurityWPA2Enterprise = 9, SecurityEnterprise = 10, SecurityWPA3Personal = 11,
        SecurityWPA3Enterprise = 12, SecurityWPA3Transition = 13, SecurityOWE = 14, SecurityOWETransition = 15;

    /// <summary>The strongest security the network supports, in Vistumbler's terms.</summary>
    static (AuthenticationType, EncryptionType) ReadSecurity(IntPtr network)
    {
        bool Supports(nint security) => SendBool(network, Sel("supportsSecurity:"), security);

        if (Supports(SecurityWPA3Enterprise)) return (AuthenticationType.WPA3_Enterprise, EncryptionType.CCMP);
        if (Supports(SecurityWPA3Personal) || Supports(SecurityWPA3Transition)) return (AuthenticationType.WPA3_PSK, EncryptionType.CCMP);
        if (Supports(SecurityWPA2Enterprise) || Supports(SecurityEnterprise)) return (AuthenticationType.WPA2_Enterprise, EncryptionType.CCMP);
        if (Supports(SecurityWPA2Personal) || Supports(SecurityPersonal)) return (AuthenticationType.WPA2_PSK, EncryptionType.CCMP);
        if (Supports(SecurityWPAEnterprise) || Supports(SecurityWPAEnterpriseMixed)) return (AuthenticationType.WPA_Enterprise, EncryptionType.TKIP);
        if (Supports(SecurityWPAPersonal) || Supports(SecurityWPAPersonalMixed)) return (AuthenticationType.WPA_PSK, EncryptionType.TKIP);
        if (Supports(SecurityOWE) || Supports(SecurityOWETransition)) return (AuthenticationType.OWE, EncryptionType.CCMP);
        if (Supports(SecurityWEP) || Supports(SecurityDynamicWEP)) return (AuthenticationType.Open, EncryptionType.WEP);
        if (Supports(SecurityNone)) return (AuthenticationType.Open, EncryptionType.None);
        return (AuthenticationType.Unknown, EncryptionType.Unknown);
    }

    /// <summary>The newest 802.11 standard the network supports (CWPHYMode), else its band.</summary>
    static string ReadRadioType(IntPtr network, int band)
    {
        (nint Mode, string Name)[] modes = [(7, "802.11be"), (6, "802.11ax"), (5, "802.11ac"), (4, "802.11n"), (3, "802.11g"), (1, "802.11a"), (2, "802.11b")];
        foreach (var (mode, name) in modes)
            if (SendBool(network, Sel("supportsPHYMode:"), mode)) return name;
        return band switch { 1 => "802.11 2.4 GHz", 2 => "802.11 5 GHz", 3 => "802.11 6 GHz", _ => "802.11" };
    }

    /// <summary>CoreWLAN can drop leading zeros ("0:1b:..."); Vistumbler uses XX:XX:XX:XX:XX:XX.</summary>
    static string? NormalizeBssid(string? bssid)
    {
        if (string.IsNullOrWhiteSpace(bssid)) return null;
        var parts = bssid.Split(':');
        return parts.Length == 6 ? string.Join(":", parts.Select(p => p.PadLeft(2, '0').ToUpperInvariant())) : bssid.ToUpperInvariant();
    }

    /// <summary>CWChannelBand: 1 = 2.4 GHz, 2 = 5 GHz, 3 = 6 GHz.</summary>
    static int ChannelToFrequency(int channel, int band) => band switch
    {
        1 => channel == 14 ? 2484 : 2407 + channel * 5,
        2 => 5000 + channel * 5,
        3 => 5950 + channel * 5,
        _ => 0
    };

    // -30 dBm = 100%, -90 dBm = 0% (the same scale as the Android scanner and the original Vistumbler)
    static int RssiToPercent(int rssi) => rssi <= -90 ? 0 : rssi >= -30 ? 100 : (rssi + 90) * 100 / 60;

    public Task<List<WiFiAdapter>> GetAvailableAdaptersAsync()
    {
        using var pool = new NSAutoreleasePool();
        var adapters = new List<WiFiAdapter>();
        var names = Send(ClientClass.Value, Sel("interfaceNames"));
        foreach (var name in Runtime.GetNSObject<NSSet>(names)?.ToArray<NSObject>() ?? [])
            adapters.Add(new WiFiAdapter { Id = name.ToString() ?? string.Empty, Name = $"Wi-Fi ({name})" });
        return Task.FromResult(adapters);
    }

    public void SetActiveAdapter(string adapterId) => _activeInterface = adapterId;

    public Task<string?> GetConnectedBssidAsync()
    {
        using var pool = new NSAutoreleasePool();
        var iface = GetInterface();
        return Task.FromResult(iface == IntPtr.Zero ? null : NormalizeBssid(Str(Send(iface, Sel("bssid")))));
    }
}
#endif
