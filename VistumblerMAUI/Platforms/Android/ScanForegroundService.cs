#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using VistumblerMAUI.Services;
using AndroidApp = Android.App.Application;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// Foreground service that keeps Wi-Fi scanning and GPS callbacks alive while the
/// screen is off or the app is backgrounded (the WiGLE / vistumbler-android model:
/// persistent notification + partial wakelock). Started/stopped by
/// <see cref="AndroidKeepAliveService"/> whenever scanning or GPS toggles.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public class ScanForegroundService : Service
{
    private const int    NotificationId = 0x5CA7;
    private const string ChannelId      = "scanning";

    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var mgr = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && mgr.GetNotificationChannel(ChannelId) is null)
            mgr.CreateNotificationChannel(
                new NotificationChannel(ChannelId, "Scanning", NotificationImportance.Low));

        // Tapping the notification brings the app back to the foreground.
        var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!);
        var openIntent = launch is null ? null
            : PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Vistumbler")
            .SetContentText("Scanning for access points…")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(openIntent)
            .SetOngoing(true)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);

        // Partial wakelock: the foreground service keeps the process alive, but CPU
        // doze with the screen off can still stall the 1 s scan/GPS loops without it.
        if (_wakeLock is null)
        {
            var pm = (PowerManager)GetSystemService(PowerService)!;
            _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "Vistumbler:scan");
            _wakeLock?.Acquire();
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        try { _wakeLock?.Release(); } catch { /* already released */ }
        _wakeLock = null;
        base.OnDestroy();
    }
}

/// <summary>Starts/stops <see cref="ScanForegroundService"/>; registered as the
/// Android <see cref="IKeepAliveService"/>.</summary>
public class AndroidKeepAliveService : IKeepAliveService
{
    public void Start()
    {
        // Best-effort notification permission (Android 13+). The service runs either
        // way — without it the notification is just hidden from the shade.
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await Permissions.RequestAsync<Permissions.PostNotifications>(); }
            catch { /* not critical */ }
        });

        var ctx = AndroidApp.Context;
        var intent = new Intent(ctx, typeof(ScanForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) ctx.StartForegroundService(intent);
        else ctx.StartService(intent);
    }

    public void Stop()
    {
        var ctx = AndroidApp.Context;
        ctx.StopService(new Intent(ctx, typeof(ScanForegroundService)));
    }
}
#endif
