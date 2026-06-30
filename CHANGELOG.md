# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.3.1
### ✨ Features and improvements
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
