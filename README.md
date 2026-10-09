# Vistumbler MAUI

> **Experimental preview.** Vistumbler MAUI is still being developed: expect missing features, bugs and changes between versions. For everyday scanning on Windows, the stable release is [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler).

Vistumbler MAUI brings the [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler) WiFi scanner to more platforms. It's built with .NET MAUI, so the same app runs on **Windows and Android**: you can scan from a phone and map access points with its GPS.

Website: [vistumbler.net/maui](https://www.vistumbler.net/maui/) · Forum: [forum.techidiots.net](https://forum.techidiots.net/forum/) · Changelog: [CHANGELOG.md](CHANGELOG.md)

## Download

Get the latest version from the [releases page](https://gitlab.techidiots.net/techidiots-llc/VistumblerMAUI/-/releases) (also mirrored to [GitHub](https://github.com/acalcutt/VistumblerMAUI/releases)). Each release has:

| File | What it is |
| :--- | :--- |
| `VistumblerMAUI-vX.Y.Z-android.apk` | Android app for phones and tablets |
| `VistumblerMAUI-vX.Y.Z-win-x64-setup.exe` / `-win-arm64-setup.exe` | Windows installer, with a Start Menu shortcut and uninstaller (recommended) |
| `VistumblerMAUI-vX.Y.Z-win-x64.zip` / `-win-arm64.zip` | Portable Windows copy: unzip and run `VistumblerMAUI.exe` |

The Windows downloads are self-contained, so there's no separate .NET or Windows App SDK to install. To install the APK, allow "Install unknown apps" for your browser or file manager. Newer APKs install over older ones and keep your data.

### Requirements

* **Android** 8.0 or later. Location permission is needed for WiFi scanning and GPS.
* **Windows** 10 or 11, x64 or ARM64, with a WiFi adapter. A GPS receiver is optional.

## Features

* Live access point list with details for each network
* Map with your GPS position and track, live access points, WiFiDB history layers, 3D terrain and offline map caching
* GPS from the device's location service, or a serial NMEA receiver on Windows
* Background scanning on Android, and an option to keep the screen on while scanning
* Channel graph
* Import and export of scans, including the GPS track, to a folder you choose
* [WiFiDB](https://wifidb.net/wifidb/) account support, including registration by QR code or link

## Using Vistumbler MAUI

* **Tabs:** **Scan** (access point list), **Map**, **Channels** and **Settings**. Start and stop scanning and GPS from the bar at the top.
* **Menu** (☰): Import, Export, New Session, Check for Updates, and the two exit options.
* **Sessions:** scans are saved per session. On startup you can resume a previous session or start a new one. **Exit (Save DB)** keeps the session, while **Exit** discards it.
* **Updates:** checks for a new version at startup, or on request from the menu or **Settings → Updates**. You can turn the startup check off there, and choose whether to include pre-releases. On Windows, an installed copy downloads and runs the new installer, then restarts with your session kept. On Android, the new APK opens in your browser to install.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the MAUI workloads. Android builds also need the Android SDK, which Visual Studio installs.

```bash
dotnet workload restore VistumblerMAUI.sln

# Windows
dotnet build VistumblerMAUI/VistumblerMAUI.csproj -f net10.0-windows10.0.19041.0

# Android (debug-signed APK)
dotnet build VistumblerMAUI/VistumblerMAUI.csproj -f net10.0-android36.0
```

Or open `VistumblerMAUI.sln` in Visual Studio 2022 or later and pick a target. The iOS target builds for the simulator only and isn't released.

### Project structure

```
VistumblerMAUI.sln
├── Vistumbler.Core/        Shared models and service interfaces (plain .NET, no MAUI)
├── VistumblerMAUI/         The MAUI app
│   ├── Views/, ViewModels/ Pages and their view models (MVVM, CommunityToolkit.Mvvm)
│   ├── Services/           Database (SQLite), sessions, GPS, import/export, map styles, WiFiDB, updates
│   ├── Controls/           Scan bar, signal and channel graph drawables
│   └── Platforms/          Android, Windows and iOS code (WiFi scanning, GPS, background service)
└── build/                  Windows installer (NSIS) and code-signing scripts used by CI
```

Maps are drawn by [MapLibreNative.Maui.Handlers](https://www.nuget.org/packages/MapLibreNative.Maui.Handlers).

### Releases

GitLab CI (`.gitlab-ci.yml`) builds every merge request and push to `main`, producing Windows zips and a debug-signed APK as job artifacts. When `main` has a version (`ApplicationDisplayVersion` in the csproj) with no release yet, CI publishes one. It contains the Windows zips and installers, signed when a certificate is configured, and an APK signed with the release keystore. The release notes come from `CHANGELOG.md`. To make a release, run a pipeline on `main` with `BUMP_VERSION` set (for example `patch`, or `prerelease` for an `-rc` build). This opens a merge request that bumps the version and changelog.

The Android release must always be signed with the same keystore (CI variables `ANDROID_KEYSTORE_*`). Otherwise users can't install updates over their existing app.

## Contributing and support

* **Bugs and feature requests:** open an [issue](https://gitlab.techidiots.net/techidiots-llc/VistumblerMAUI/-/issues), or post on the [forum](https://forum.techidiots.net/forum/).
* **Code:** merge requests are welcome. Please follow the existing code style and describe your change in the `master` section of [CHANGELOG.md](CHANGELOG.md).

## Related projects

* [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler): the original, stable AutoIt version for Windows
* [Vistumbler CS](https://gitlab.techidiots.net/techidiots-llc/VistumblerCS): an experimental C# rewrite for Windows
* [WiGLE WiFi Wardriving](https://github.com/wiglenet/wigle-wifi-wardriving): the Magic 8 Ball (m8b) offline-location format comes from it; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

Created by Andrew Calcutt, [TechIdiots LLC](https://www.techidiots.net).
