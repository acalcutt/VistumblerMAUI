# Changelog

## master
### ✨ Features and improvements
- **An AP's signal and range maps (Map on its details page)** — the original's KML "Selected AP" maps, drawn on the built-in map instead of in Google Earth: every place the AP was heard, coloured from red (weak) to green (strong) in the original's six signal bands, its signal circle (100 + RSSI metres around where it was strongest) and its range circle (out to the farthest place it was heard). The ✕ on the bar above the map clears them.
- **NetStumbler text (wi-scan) import** — NetStumbler's text export now imports, under Netstumbler on the Import page; it was offered before but imported nothing, since only the binary .ns1 was read. Text or binary is told apart by the file's content, as in the original. Each sighting becomes part of the AP's signal history, placed where its signal was strongest.
- **WarDrive (Android) .db3 import** — the WarDrive app's database now imports, under Wardrive on the Import page, with each network's security, band, position and time. The option read every file as CSV before.
- **Record the GPS track even when no APs are found (Settings → GPS)** — the original's "Save all GPS data", on by default like there: GPS points are kept when a scan finds no APs, and while GPS is on without scanning, so exported tracks (VS1, KML, GPX) have no gaps.

- **Filters** — the original's View → Filters: saved filters in a list (the Filter button on the Scan page), one of which narrows the AP list at a time, and the Export page can export only the APs it shows (the original's "Filtered APs" exports). Simple fields suit a phone: SSID contains, BSSID starts with, open/WEP/secure, 2.4/5/6 GHz, channels, minimum signal, active or dead. **Advanced rules** take the original's per-field syntax (lists `1,6,11`, ranges `1-6` or `-80--60`, `<>` for not, `%` wildcards), with one change: a value with a dash such as `WPA2-Personal` now matches itself instead of being read as a range, which matched nothing unless written `WPA2\-Personal`. APs leave the list as they stop matching, e.g. when they go dead under an "active only" filter.
- **Group by on the Scan page** — the phone version of the original's tree view: group the AP list by channel, band, security type (open/WEP/secure), authentication, encryption, network type, manufacturer or SSID. Each group shows how many APs it has and collapses or expands with a tap; search, filter and sort apply within the groups.
- **Clear (menu)** — replaces Save & Clear in the menu: clears the AP list, asking whether to save it to a file first. The Clear button on the Scan page, which cleared without asking, is now the Filter button.
- **The last session reopens by itself** — with a single earlier session, the app opens it where you left off instead of asking. With several, it still asks; and if reopening a session automatically ever crashes the app, the next start asks instead of trying again.
- **Speak RSSI, or a tone (Settings → Sound)** — Speak signal can say the RSSI ("minus 65 dBm") instead of the signal percentage, and a third voice plays a short tone whose pitch rises with the signal, in place of the original's MIDI notes.
- **Settings reordered** — Scanning, Map, GPS and WifiDB come first, then Save & Clear, Sound and the rest. The map AP colors moved to their own page (Map AP colors… under Map), since the grid took up much of Settings.
- **Offline maps under the map** — Save Area and Go Offline moved from the toolbar to the buttons under the map as **Save map for offline** (now explaining what it downloads before it starts) and **Saved maps only**. New Session moved to the top of the menu.
- **Wi-Fi position, offline (GPS details)** — an estimate of where you are from the APs in range, with no GPS or network, e.g. indoors: each AP votes for the 1 km squares it has been seen in, and the square most agree on is shown, with how far it is from the GPS fix and **Show on map** to see the squares. It searches the open session's APs and any imported **Magic 8 Ball (.m8b)** files, the format from WiGLE WiFi Wardriving, so their exports work too.
- **Export to M8b** — writes a session as a Magic 8 Ball file (each AP's best-signal position), to import on another device for offline position.

### 🐞 Bug fixes
- **Map dots no longer vanish during long scans** — the live AP layer was built by pasting text together, which let an SSID with control characters (hidden networks often broadcast \0 bytes) or a comma-decimal phone language produce invalid JSON, and then every dot disappeared. It's now written with a JSON writer.
- **KML and NetXML exports no longer fail on hidden-network SSIDs** — characters XML can't hold made the whole export fail; they're now left out of the file.
- **Update manufacturers works on Android** — IEEE refuses Android's default user agent (HTTP 418); the app now identifies itself.
- **WEP networks are shown and exported as WEP** — the map colored them as open, and VS1 exports gave them the open security type, because WEP networks report Open authentication. WEP is now told by its encryption, as in the original.
- **6 GHz channels from WiGLE CSV, Kismet and WarDrive imports** — 6 GHz networks came in as channel 0.
- **Wi-scan files written by Vistumbler keep their signal on import** — the original's import treated every wi-scan file as NetStumbler's (dBm = SNR - 95), while its own wi-scan export writes Sig = dBm + 50, so its files came back 95 dB too weak. Files whose creator is Vistumbler are now read with the export's formula.

## 0.7.0
### ✨ Features and improvements
- **Start scanning and GPS when the app opens (Settings → Scanning)** — the original's "Auto Scan APs on launch", plus the same for GPS. Both off by default.
- **Choose the Wi-Fi adapter on Windows (Settings → Scanning)** — the original's Interface menu: scan with one adapter, or all of them (the default).
- **Coordinate format (Settings → GPS)** — coordinates can be shown as decimal degrees (the default), or in the original's three styles: `dd.dddd` (N 48.1173000), `ddmm.mmmm` (N 4807.0380) and `dd mm ss` (N 48° 7' 2.28").
- **Copy an AP's details** — Copy on the AP details page copies all its details, or just the BSSID, SSID or GPS position, like the original's Copy.
- **Links (menu)** — the Vistumbler website, wiki and forum, the VistumblerMAUI source code, and the WifiDB website and live AP page, as in the original's Help and WifiDB menus. Donate and Store are there too, except in the Google Play build.
- **Manufacturers for scanned APs** — the Manufacturer column now fills in while scanning, from the first three bytes of each BSSID, instead of only for imported files. A list of about 40,000 manufacturers (IEEE's) comes with the app, so it works offline from the first scan, and **Update manufacturers** in Settings → Data downloads IEEE's current list, like the original's Update Manufacturers and VistumblerCS's. Saved sessions, imports and exports get names filled in too.
- **Sounds** — the original's sounds now play: new_ap.wav when new APs are found (once per scan, once per new AP, or once per AP louder for a stronger signal), and error.wav when the GPS receiver has a problem (at most every 30 seconds). Settings → Sound. The "Sound alerts" switch was there before but nothing played.
- **Speak signal** — on an AP's details page, **Speak signal while scanning** says its signal every few seconds, with the device's voice or the original's recorded words, so you can walk towards an AP without watching the screen. Voice, interval and "percent" are in Settings → Sound.
- **Import a folder** — the Import page can import every file of the chosen type in a folder, like the original's Import Folder. On Android the folder comes from the system picker.
- **Look an AP up in WifiDB** — WifiDB on an AP's details page shows what WifiDB has for it (first and last seen, best signal, position), with a link to its WifiDB page. The original's Locate in WifiDB posted to an API that no longer answers this; this uses WifiDB's search.
- **GPS details (menu)** — the original's GPS Details and GPS Compass windows on one page: position, altitude, satellites, accuracy, speed, heading, fix quality and time, and a compass pointing along your direction of travel.

### 🐞 Bug fixes

## 0.6.0
### ✨ Features and improvements
- **Saves that couldn't be uploaded to WifiDB are retried** — with Save & Clear's "Upload each save to WifiDB" on, a file that can't be uploaded (no internet, WifiDB unreachable, or no account set up yet) now waits in a queue instead of being forgotten. The queue is kept across restarts and retried when the connection comes back, when the app starts, after each save, and on **Retry now** in Settings → Save & Clear, which shows how many files are waiting and why. A file WifiDB already has (an earlier attempt got through) leaves the queue rather than being sent again. Automatic saves no longer wait for the upload, and an upload gives up after 2 minutes on a bad connection instead of 10 and stays queued.
- **Delete saved files once WifiDB has them (Settings → Save & Clear)** — an option, off by default, to remove each saved file after WifiDB has accepted it, so the save folder doesn't fill up when everything goes to WifiDB anyway. WifiDB keeps its own copy of every upload.
- **Auto Save And Clear now defaults to 5000 APs** — the original Vistumbler's 1000 was set by how slow its list became; this app copes with far more. A number you already set is kept.

### 🐞 Bug fixes
- **Choosing a folder on the phone for Save & Clear or Export works** — Android asked for access and then the app said it couldn't write there, because it wrote by file path, which Android's scoped storage doesn't allow for most of shared storage. The app now writes through the access Android grants for the chosen folder, which it keeps across restarts, and shows the folder by name (e.g. "Phone storage/Documents/Vistumbler").

## 0.5.0
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.4.0
### ✨ Features and improvements
- **Save & Clear (menu)** — as in Vistumbler and VistumblerCS: saves the access points to a VS1 (or zipped VSZ) file, then clears the list while scanning carries on. Nothing is cleared unless the file was written. Afterwards the file can be shared, since the default folder on Android is private to the app.
- **Auto Save And Clear (Settings → Save & Clear)** — runs Save & Clear by itself once the list holds a set number of APs (1000 by default) or after a set number of minutes of scanning (60 by default), like the original's Auto Save And Clear. The same section sets the folder, the file name (saved as `2026-10-07 14-30-05_AutoSave.VS1`, as the original names them) and the file type.
- **Upload to WifiDB (menu)** — sends the current session to WifiDB's import API with a title, notes and other users, as the original Vistumbler and VistumblerCS do, using the account in Settings → WifiDB. WifiDB queues the file; **Check import status** follows it until it is imported. Save & Clear can also upload each file it saves (Settings → Save & Clear → Upload each save to WifiDB).
- **Bluetooth GPS receivers on Android (Settings → GPS)** — besides the phone's own GPS, the app can now read an external Bluetooth GPS receiver that sends NMEA, like the COM-port receiver option on Windows. Pair it in Android's Bluetooth settings, then choose it under Settings → GPS; Android 12 and later ask for the Nearby devices permission the first time. If the receiver drops out or is switched off, the app reconnects to it every few seconds while GPS is on.
- **USB GPS receivers on Android (Settings → GPS)** — a GPS receiver plugged in through a USB OTG adapter now works without any driver install, including GlobalSat's Prolific PL2303 receivers (BU-353) and ones with Silicon Labs CP210x, FTDI, CH340 or u-blox chips, using the [UsbSerialForAndroid](https://github.com/anotherlab/UsbSerialForAndroid) library. Choose the baud rate there (4800 for GlobalSat, 9600 for most u-blox). Android asks to let the app use the receiver the first time it connects; nothing is asked unless the USB source is chosen. Unplugging and replugging is picked up by itself while GPS is on.
- **GLONASS, Galileo and BeiDou receivers** — external receivers (USB, Bluetooth, and the COM port on Windows) are read whatever satellite system they report: GPS ($GP), GLONASS ($GL), combined ($GN), Galileo ($GA) or BeiDou ($GB). Previously only $GP and $GN sentences were read.
- **GPS receiver timeouts, as in the original Vistumbler (Settings → GPS)** — for external receivers (COM port, Bluetooth, USB), two options that are on by default: **Reconnect the receiver when no data is received for 10 seconds** (the original's "Disconnect GPS…", which here reconnects instead of turning GPS off; turn it off for a receiver that pauses its output), and **Reset GPS position when no fix is received for 30 seconds**, so APs found after the receiver loses its fix aren't placed at its last position. The phone's own GPS isn't affected, since it can go quiet while standing still.
- **AP point size on the map (Settings → Map)** — scales the access point dots, live scan and WifiDB history, from 50% to 300%.

### 🐞 Bug fixes
- **A COM-port GPS on Windows no longer falls behind** — each burst of data from the receiver read only one NMEA sentence, so with several sentences a second the backlog grew and the position lagged further and further behind. All waiting sentences are now read.
- **The Android splash screen shows the Vistumbler logo** — the app replaced MAUI's splash theme with a plain one, so the splash image was never used and Android squeezed the launcher icon into its place instead. The app now uses MAUI's splash theme with the Vistumbler logo, centred on white and padded so Android 12+ doesn't clip its round edge.
- **Settings → GPS no longer says "Windows Location" on Android** — the phone's own GPS is now called Phone GPS there, and the Windows-only serial COM-port option is no longer offered on Android. Windows keeps Windows Location and Serial NMEA.
- **No more crash the first time Use GPS or Scan APs is pressed on Android** — the background scanning service started while the location permission prompt was still open, and Android 14+ closes an app that starts a location service without that permission. The app now asks for location permission first and starts the service only once it is granted. Scan APs asks for it too (Android returns no Wi-Fi results without it) and says so if it is refused, instead of scanning with nothing to show.
- **The map's 3D terrain button has its icon again on Android** — the button was there but blank. The map renderer never drew its icon on Android; MapLibreNative.Maui.Handlers 5.0.0-pre.3 fixes that, and the app now uses it (from 5.0.0-pre.2). Windows was not affected.

## 0.3.12
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.3.11
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.3.10
### ✨ Features and improvements
- Test Release

## 0.3.9
### ✨ Features and improvements
- Test Release

## 0.3.9-pre.1
### ✨ Features and improvements
- **History overlays keep working when wifidb.net cannot be reached** — the age-tier and cell layers still ask WifiDB for each bucket's TileJSON, which now points them at that bucket's published PMTiles archive on data.wifidb.net. If WifiDB itself is unreachable, the app now goes to those archives directly instead of drawing nothing, using a built-in address per bucket. Each fallback address carries the bucket's `.torrent` and magnet in its fragment — unused for now, so the handles are already in place when peer-to-peer tile loading arrives. The magnets name a bucket rather than one nightly build, so they stay valid as the archives are rebuilt.
- **New "Data URL" setting (Settings → WifiDB)** — the origin those archives are fetched from, separate from the site's Base URL because it is a different host. Defaults to `https://data.wifidb.net`; point it at a mirror to move the fallback.
- **Export can choose where the file goes** (Export → Location) — the page offered a file name and nothing else, so every export landed in the app's own documents folder. **Browse…** now opens the platform's folder picker and the choice is remembered; **Use default folder** puts it back.
  A picked folder is proven writable before it is kept, and again before each export — not afterwards. On Android a folder chosen through the system picker comes back as a Storage Access Framework tree whose reported path scoped storage often will not let the app write to, so the check is what stops that becoming an export that reports success and writes nothing: a folder that cannot be written to is refused with the reason on screen, and one that stops working later falls back to the default folder with the export saying so. The file is still offered to the share sheet either way, which remains the reliable way to get it off a phone.
- **History layers read from the published archives directly** — WifiDB's map now addresses its pmtiles-swarm archives itself rather than going through `tilejson.php`, and the app follows. It was already reaching the same archives, since that endpoint answers 302 to them, so what changes is that it asks for them straight away: one request instead of two, and the history layers still draw when wifidb.net is down but the archives are up. The layer inside each archive is named for its bucket — the same name the endpoint reported — so every source-layer and colour is untouched.
  Each URL now carries the archive's `.torrent` and magnet in its fragment, which is the part a redirect could never give the app: a fragment is not sent in a request, so the HTTP stack that follows the redirect never surfaces it. Nothing reads them yet and no torrent is fetched — they are there so peer-to-peer tile loading can be switched on later without every URL changing again. The `.torrent` handle points at WifiDB's own per-bucket endpoint rather than being built from the magnet's infohash, which named one build and went stale as the archives were rebuilt.
  `cell_networks` has no published archive and keeps the endpoint, as does everything if the archives cannot be reached at all. The **Data URL** setting still chooses where they are fetched from.
- **3D terrain button on the map** — the renderer's terrain control, shown only when terrain is actually available. It drapes the basemap over elevation and is a plain toggle: on, then off again.
  Which DEM it uses is decided per style, because getting it wrong is worse than not offering the button. A style that declares its own raster-dem is draped over that one — it is the source the style's hillshade and relief layers already draw from, so the relief matches what is drawn and nothing extra is fetched. The WifiDB styles each carry several DEMs, and the choice is not arbitrary: picking the first would drape the map over GEBCO bathymetry (ocean depths) rather than land, so a source the style names as terrain wins, then a `terrain`-named one, then whatever single DEM is there.
  A style with no DEM at all still gets the button — the app adds one itself, defaulting to WifiDB's own terrain tiles, the same elevation the relief styles use. [Mapterhorn](https://mapterhorn.com/data-access/) and the [AWS Open Data terrain-tiles](https://registry.opendata.aws/terrain-tiles/) are built in beside it. So the button works on a custom style too, not just the presets.
- **Map renderer updated to MapLibreNative.Maui.Handlers 5.0.0-pre.2** (from 4.4.0), replacing the temporary local 4.5.0 test build the follow-zoom work was using — that feature is now in a published release, so the local package feed is gone from `nuget.config`. The renderer renamed its low-level binding types `Mbgl*` → `Mln*`, so the one place this app reaches past the map control — the offline manager and the online/offline toggle — now uses `MlnOfflineManager` and `MlnNetwork`; the map control's own API is unchanged. The Android build also gains a third ABI, `armeabi-v7a`, so the app now runs on 32-bit ARM devices. Nothing else here changes behaviour: the release's 3D terrain, Vulkan rendering path and tile-template sources are all unused so far.
- **GPS follow zoom setting (Settings → Map)** — controls the zoom applied when the map's GPS button enters Follow mode, like vistumbler-android's zoom-to-location. **Auto (fit GPS accuracy)** (default) picks a level where the fix's accuracy circle spans about a third of the screen — a sharp fix lands at street level, a coarse fix stays zoomed out; **Manual zoom level** always eases to a chosen level (1–22); **Keep current zoom** preserves the old behaviour. Pinch-zooming while following sticks until Follow is re-entered. Uses the map renderer's new `GpsFollowZoomMode`/`GpsFollowZoom` (MapLibreNative.Maui.Handlers 5.0.0-pre.2).

### 🐞 Bug fixes

- chore(deps): update the map renderer to MapLibreNative.Maui.Handlers … ([#3](https://github.com/acalcutt/VistumblerMAUI/pull/3)) (@acalcutt)
- **Export failed on Android with `IO_PathNotFound_Path`** — every format, every time. `MyDocuments` on Android is `/data/user/0/<package>/files/Documents`, and `Environment.GetFolderPath` hands back that path whether or not it exists; nothing had ever created it, so the write failed against a directory the app itself owns. Now created before writing. The finished file is also offered to the share sheet, because that directory is app-private internal storage — no file manager can see it and no browser can attach it, so an export that "succeeded" still left the data out of reach.

## 0.3.8
### ✨ Features and improvements
- **Reworked GPS map buttons: separate tracking and bearing controls** — updated the map renderer to the released MapLibreNative.Maui.Handlers 4.4.0 (replacing the temporary local 4.3.0 test build). The top GPS button now cycles the tracking mode Off ○ → Show ⊙ → Follow ◎ (the combined follow-bearing state is gone), and the bottom button (previously a plain reset-to-north) cycles the camera bearing mode Free ↺ → North-up N → GPS bearing ➤ — matching vistumbler-android's two-button model. Panning the map while in Follow drops back to Show (one tap re-enters Follow), and rotating the map by hand drops the bearing mode back to Free. The location puck now always points in the direction of travel.
- **Map honours the OS "Font size" accessibility setting (Android)** — the map is created with the renderer's new `UiScale` set from the Android font scale, so map labels and AP circles grow with enlarged system text instead of staying tiny relative to the rest of the UI.

### 🐞 Bug fixes
- **A failed database write during a scan cycle no longer crashes the app** — a transient SQLite failure (seen in the field as "attempt to write a readonly database" after backgrounding) killed the whole app from the async scan handler. The cycle's persist is now dropped with a status-bar notice, the connection is closed so the next cycle reopens a fresh one, and the next cycle re-persists everything current.

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
