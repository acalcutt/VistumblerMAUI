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
* **Windows** 10 or 11, x64 or ARM64, with a WiFi adapter. A GPS receiver is optional. Recording Bluetooth devices needs a Bluetooth radio, and cell towers a mobile broadband (cellular) modem.

Android limits how often apps can scan for WiFi (Android 9 and later). For faster updates, turn off **WiFi scan throttling** in the phone's Developer options.

## Features

* **Scanning:** live access point list with SSID, BSSID, manufacturer, signal, channel, band and security; sort, group by channel, security and more, and saved filters; details for each network with its signal history
* **Bluetooth and cell towers:** Bluetooth and Bluetooth LE devices and cell towers recorded alongside the WiFi, each in its own list (Android, and Windows with Bluetooth or a cellular modem), and shown from imported files on any device
* **Several WiFi adapters at once** on Windows, chosen in **Settings → Scanning**
* **Background scanning** on Android, and an option to keep the screen on while scanning
* **Channel graphs** for 2.4, 5 and 6 GHz
* **GPS** from the device's location service, a Bluetooth or USB NMEA receiver on Android, or a serial NMEA receiver on Windows; a GPS details page with a compass
* **Map** with your position and track, live access points, WiFiDB history layers, 3D terrain, several map styles and offline map areas
* **Site survey:** mark spots on a floor plan, or on the map, and see a signal heat map; PNG and CSV export
* **Import and export:** Vistumbler VS1/VSZ and CSV, WiGLE CSV, KML (with signal maps), GPX, GeoJSON, Kismet and NetStumbler NS1; imports also read WarDrive files and whole folders
* **[WiFiDB](https://wifidb.net/wifidb/):** uploads with an import status page and automatic upload of saves, account setup by QR code or link, and finding an AP in WiFiDB
* **WiGLE:** optional upload of WiFi, Bluetooth and cell towers alongside WiFiDB (off by default)
* **Sounds** for new access points and spoken signal strength
* **18 languages** (Settings → Language): English plus translations seeded from the original Vistumbler's language files, the rest machine-translated drafts awaiting review

## Using Vistumbler MAUI

* **Tabs:** **Scan** (access point list), **Map**, **Channels** and **Settings**. Start and stop scanning and GPS from the bar at the top.
* **Menu** (☰): New Session, Import, Export, Clear, Upload to WifiDB, Uploads, Check for Updates, GPS details, Site survey, Links, and the two exit options.
* **Sessions:** scans are saved per session. On startup you can resume a previous session or start a new one. **Exit (Save DB)** keeps the session, while **Exit** discards it.
* **Updates:** checks for a new version at startup, or on request from the menu or **Settings → Updates**. You can turn the startup check off there, and choose whether to include pre-releases. On Windows, an installed copy downloads and runs the new installer, then restarts with your session kept. On Android, the new APK opens in your browser to install. A copy installed from Google Play is updated by Play instead, so it has no update check.

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

## License

Vistumbler MAUI is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 2 of the License, or (at your option) any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text. Code adapted from other projects keeps its own license; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Created by Andrew Calcutt, [TechIdiots LLC](https://www.techidiots.net).
