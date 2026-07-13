# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.3.8
### ✨ Features and improvements
- **Reworked GPS map buttons: separate tracking and bearing controls** — updated the map renderer to the released MapLibreNative.Maui.Handlers 4.4.0 (replacing the temporary local 4.3.0 test build). The top GPS button now cycles the tracking mode Off ○ → Show ⊙ → Follow ◎ (the combined follow-bearing state is gone), and the bottom button (previously a plain reset-to-north) cycles the camera bearing mode Free ↺ → North-up N → GPS bearing ➤ — matching vistumbler-android's two-button model. Panning the map while in Follow drops back to Show (one tap re-enters Follow), and rotating the map by hand drops the bearing mode back to Free. The location puck now always points in the direction of travel.

## 0.3.7
### ✨ Features and improvements
- **Android launcher icon** — the app now ships vistumbler-android's adaptive icon set (per-density webps + adaptive-icon manifest) on Android, replacing the generated MAUI icon; the foreground-service notification uses the same icon.

### 🐞 Bug fixes
- **Attribution banner no longer reopens every few seconds on the map** — updated the map renderer to MapLibreNative.Maui.Handlers 4.2.1, whose attribution handling only re-expands the banner when the attribution content actually changes. Previously the live AP layer's periodic GeoJSON refresh made the banner pop open on every update.
- **GPS track records finer detail** — the track's minimum-movement threshold now scales with the GPS fix's reported accuracy (2–10 m instead of a fixed 5 m), so corners and curves draw smoother with a good fix while stationary jitter still can't scribble.

## 0.3.6
### ✨ Features and improvements
- **GPS track on the map** — new yellow "Enable Track" / "Clear Track" buttons in the map layer bar draw a live breadcrumb line (bright yellow over a dark casing) of where you've been. Points are only added after ~5 m of real movement so a stationary device doesn't grow the track, and the line breaks into separate segments when fixes stop for over 3 minutes instead of drawing a straight connector across the gap. Clearing the track never touches the recorded GPS history.
- **Background scanning (Android)** — while scanning or GPS is on, the app now runs a foreground service with a partial wakelock (the WiGLE / vistumbler-android model, with a persistent notification), so Wi-Fi scanning and GPS keep collecting with the screen off or another app in front. Stops automatically when both Scan and GPS are off.
- **"Include GPS track" export option** — KML and GPX exports show a switch (on by default) controlling whether the session's GPS track (`<trk>` / LineString) is embedded alongside the AP placemarks.
- **Keep screen on while scanning** — optional setting (Settings → Advanced) that holds the display awake while scanning/GPS runs.
- **Scan/GPS buttons show their action** — "Scan APs" reads "Stop" and "Use GPS" reads "Stop GPS" while active, alongside the existing color change.
- **Live AP dots scale with zoom** — the live-scan circle radius now grows toward street-level zoom like the history layers (fixed 4 px dots were nearly invisible on high-DPI phones).
- **Debug logging toggle** — Settings → Advanced → "Debug logging" (off by default) gates the chatty diagnostics (per-fix GPS lines, AP layer refreshes, camera idle) for field troubleshooting via `adb logcat`.

### 🐞 Bug fixes
- **Android: GPS position now updates continuously** — MAUI's Geolocation foreground listener was observed delivering a single fix and then going permanently silent (Samsung S24 / Android 16), which froze the map puck and left scans without positions until GPS was toggled off/on. Android now drives LocationManager directly, subscribing to every live provider (fused + gps) at 1 s intervals, the same multi-provider pattern WiGLE uses.
- **Android: map puck appears immediately on the Map tab** — the freshly-created map controller is seeded with the latest known fix on style load, so Follow/Follow-bearing modes engage without waiting for the next OS fix (or restarting GPS).
- **APs no longer follow the scanner** — an AP's plotted coordinates were overwritten with the device's current position on every scan cycle, dragging all previously-seen APs along with you (and stacking them all on one point when stationary). Coordinates now only update when a detection beats the AP's previous best signal, matching classic Vistumbler's strongest-signal semantics.
- **Map tap popup readable in dark mode** — the AP info popup drew theme-default (white) text on its hardcoded white card, making it look empty with system dark mode on.

## 0.3.5
### ✨ Features and improvements
- **Android touch gestures on the map** — via the renderer bump to MapLibreNative.Maui.Handlers 4.2.0, two-finger pinch-zoom, rotate and tilt now work on Android (previously only the on-screen zoom/rotate buttons did).

### 🐞 Bug fixes
- **Android: the map tab no longer crashes the app on open** — the renderer bump to 4.2.0 fixes a native stack-overflow crash that happened as soon as the Map tab was shown on Android.
- **Android: several map rendering bugs fixed** — also via 4.2.0: polygon fills no longer show a checkerboard pattern and tiles no longer show white seams; the map no longer stretches/blanks after rotating the device; panning tracks the finger correctly; and tiles now refresh when zooming into detailed (color-relief/hillshade/vector) styles instead of staying stuck on lower-zoom content.

## 0.3.4
### ✨ Features and improvements
- **Universal Android APK** — the release APK now bundles both `arm64-v8a` (phones) and `x86_64` (emulators), each with the native map engine, instead of an arm64-only build. One APK runs on physical devices and emulators alike.

### 🐞 Bug fixes

## 0.3.3
### ✨ Features and improvements
- **Self-contained Windows releases, now with ARM64** — Windows releases ship a native `win-arm64` build alongside `win-x64`, and both are self-contained: the .NET runtime and the Windows App SDK runtime are bundled, so users don't need to install anything first. ARM64 devices run the app and its native map engine (`mln-cabi.dll`) natively instead of under x64 emulation.

### 🐞 Bug fixes

## 0.3.2
### ✨ Features and improvements
- **New airspace-free map renderer (MapLibreNative.Maui.Handlers 4.1.3)** — upgraded to the 4.x renderer: Windows draws the map into a real in-tree `Image` (no floating `WS_POPUP` GL window) and Android uses a `TextureView`, so MAUI content and the nav/GPS/attribution controls layer reliably above the map on every platform. Now consumed as the released 4.1.3 package from nuget.org, so the local dev package source was removed from `nuget.config`.
- **Offline map caching** — the map now keeps a persistent tile cache, so already-viewed areas keep rendering with no network. Two page toolbar items were added: **Save Area** pre-caches the current view (current zoom + 2 levels) for offline use, and **Go Offline / Go Online** forces MapLibre to serve only cached tiles; caching progress and offline/online state are shown in the map's status line. Downloaded tiles share the live map's cache, so they render immediately.

### 🐞 Bug fixes
- **MAUI Windows: double-tapping the nav/GPS/d-pad overlay buttons no longer leaks through to the map** — via the renderer bump to 4.1.2, the overlay buttons now handle `DoubleTapped` (previously only `Tapped`), so the second click of a fast double-click no longer bubbles past the button and zooms/pans the map behind it.

## 0.3.1
### ✨ Features and improvements
- **Settings: WifiDB account section** — set the WifiDB base URL, username, and API key (masked); values persist via MAUI `Preferences` and the base URL now drives the map's history-tile requests (so a self-hosted WifiDB works). Field naming matches VistumblerCS.
- **Settings: register with WifiDB via QR code or link** — on mobile, "Register with QR code" opens the camera (via BarcodeScanning.Native.Maui) and redeems a WifiDB registration QR (`…/redeem_link.php?token=…`), auto-filling username/API key/base URL; "Register with link…" does the same from a pasted link on any platform (incl. desktop). Ported from vistumbler-android's ActivateActivity WifiDB flow.
- **Settings: Import/Export now have a Cancel button** — returns to Settings without importing/exporting, instead of leaving you stuck on the page.
- **All history layers now use WifiDB MVT vector tiles** — the Daily layer previously fetched a one-shot GeoJSON blob from `geojson.php?func=exp_daily`; it now streams the `daily` MVT bucket via `tilejson.php?bucket=daily` just like every other age tier (and like VistumblerCS), so all ten buttons share one consistent, tile-streamed code path. Removed the now-obsolete GeoJSON-only daily machinery (the dedicated source, fetch/clear handlers, and `HttpClient`).
- **Circle paint matched to VistumblerCS** — history dots now render as solid, softly-blurred circles (`circle-opacity` 1.0 + `circle-blur` 0.5, no outline) instead of semi-transparent white-ringed dots, so the MAUI and WPF clients look identical.
- **"Cells" button renamed to "Cell Networks"** to match VistumblerCS.

### 🐞 Bug fixes
- **History vector layers now actually render** — upgraded `MapLibreNative.Maui.Handlers` to 3.2.10, which fixes a maplibre-native issue where a circle layer's source-layer, when set *after* the layer was added to the style (the runtime add pattern used for every history bucket), never triggered a tile relayout — so the colored history circles rendered nothing regardless of the `circle-color` expression. (An earlier theory pinned this entirely on a WifiDB server `.htaccess` header bug; that was real and separately fixed, but the source-layer relayout fix in 3.2.10 is what makes the runtime vector layers paint.)

## 0.3.0
### ✨ Features and improvements
- **History layer buttons renamed to match canonical mvtd/tilejson bucket scheme** — labels now match WifiDB web map: Daily, Weekly, Monthly, 0–1yr, 1–2yr, 2–3yr, 3–5yr, 5–10yr, 10yr+, and a mirrored Cells button.
- **Sectype-based layer colors** — open (green), WEP (amber), and secured (red) networks each get distinct hues; color darkens as data ages, matching the WifiDB web map color scheme.
- **Age-radius gradient** — newer points render larger and older points smaller (newest=3px, oldest=1.5px), consistent with WifiDB map behavior.
- **Scanned APs displayed on map** — access points from the current scan session are plotted as a separate layer on top of history layers; active APs are rendered lighter than inactive/dead APs from the same scan.
- **History layer z-ordering preserved across toggles** — layers always insert at the correct depth (active/dead on top → daily → weekly → … → 10yr+) regardless of the order they are toggled on/off.
- **Cells button mirrors active wifi-age tiers** — toggling the Cells button on adds cell-tower data only for the age buckets that are currently visible, keeping cell and wifi layers in sync.

### 🐞 Bug fixes

## 0.2.1
### ✨ Features and improvements

### 🐞 Bug fixes
- **Fixed CI NETSDK1112 on Windows build** — removed the separate `dotnet restore` step from Windows and Android CI jobs; the combined `dotnet build` call now handles restore correctly, avoiding the "runtime pack not available" error from a prior RID-less restore.
- **Upgraded MapLibreNative.Maui.Handlers to 3.2.9** — picks up two Android crash fixes: (1) `theJVM` null in standalone NDK builds causing an alarm-thread abort, and (2) EGL context never made current (`ScopeType::Implicit` → `ScopeType::Explicit`) causing a SIGFPE divide-by-zero in the render loop.

## 0.2.0
### ✨ Features and improvements
- **Merged the Scan and APs tabs into one** — both tabs rendered essentially the same AP list at different times (live during a scan, persisted between scans), which is redundant. The surviving "Scan" tab now loads persisted APs from the database on first appearance (so the list isn't empty before you tap Start), keeps the search bar and Clear All action from the former APs tab, and still supports the Map page's "View in AP List" BSSID deep-link (now routed to `//ScanPage` instead of the removed `//AccessPointListPage`).

### 🐞 Bug fixes
