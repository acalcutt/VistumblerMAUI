# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

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
