# Feature comparison: VistumblerMDB and VistumblerMAUI

How VistumblerMAUI compares with the original AutoIt Vistumbler (VistumblerMDB, `Vistumbler.au3`), feature by
feature. Compiled from both code bases in October 2026, and updated after VistumblerMAUI 0.6.0 for the changes on
`main` since. Update it as features land.

Legend: ✅ has it · 🟡 partly, or done differently · ❌ missing · ➕ new in VistumblerMAUI

## Scanning and the AP list

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Scan APs / Use GPS buttons | ✓ | ✅ |
| Save & Clear, Auto Save And Clear | ✓ | ✅ plus a WifiDB upload queue and delete-after-upload |
| Clear All, New Session, Exit / Exit (Save DB) | ✓ | ✅ |
| Auto Recovery VS1 / recover a crashed session | ✓ | 🟡 each session's database is kept and can be resumed from the session chooser |
| Sort, Auto Sort | ✓ | ✅ sort field and direction; new APs are inserted in sorted position |
| Filters (named, add/remove) | ✓ | ❌ only a search box (SSID, BSSID, manufacturer) |
| Tree view (APs grouped by channel, security and so on) | ✓ | ❌ |
| Copy AP details | ✓ | ✅ Copy on the AP details page: all details, BSSID, SSID or GPS position |
| Select connected AP, auto-select connected or highest signal | ✓ | ❌ |
| Add new APs to top, auto-scroll | ✓ | ❌ (sorting covers part of this) |
| Manufacturer lookup during a scan, Update Manufacturers | ✓ | ✅ IEEE list of about 40,000 comes with the app; Update manufacturers in Settings → Data |
| MAC labels | ✓ | ❌ |
| Choosing the Wi-Fi adapter (Interface menu) | ✓ | ✅ Windows, in Settings → Scanning |
| Auto Scan APs on launch | ✓ | ✅ plus turning GPS on at launch |
| Scanning with the screen off | n/a | ➕ on Android, through a background service |

## Graphs

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Graph 1 / Graph 2 (selected AP's signal over time) | ✓ in the main window | 🟡 a signal history graph on the AP details page |
| 2.4 GHz / 5 GHz channel graphs | ✓ experimental | ✅ plus 6 GHz |
| Use RSSI in graphs, graph dead time | ✓ | ❌ |

## GPS

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Serial COM-port NMEA receiver | ✓ | ✅ Windows only |
| Disconnect after 10 s / reset position after 30 s without data | ✓ | ✅ reconnects instead of turning GPS off |
| GPS coordinate display format (ddmm.mmmm and others) | ✓ | ✅ decimal plus the original's three formats |
| GPS Details and GPS Compass windows | ✓ | ✅ one GPS details page with a compass |
| Save all GPS data, even with no APs | ✓ | ❌ GPS is only recorded once per scan cycle |
| Phone GPS, Bluetooth and USB receivers, GLONASS/Galileo/BeiDou | n/a | ➕ |
| Camera trigger script | ✓ experimental | ❌ |

## Map, replacing Google Earth

| Feature | VistumblerMDB (Google Earth) | VistumblerMAUI (built-in MapLibre map) |
|---|---|---|
| Live view of active/dead APs and the GPS track | Auto KML + network link | ✅ live map with active/dead colours, track and follow modes |
| Open the KML network link | ✓ | 🟡 no longer needed, since the map is built in |
| Export to KML, all APs | ✓ | ✅ |
| Export to KML, filtered | ✓ | ❌ there are no filters |
| Signal and range circle maps for a selected AP | ✓ | ❌ |
| WifiDB history layers (age buckets) and cell layer | n/a | ➕ |
| Basemap styles, 3D terrain, saving map areas for offline use, AP colours, point size | n/a | ➕ |

## Import and export

| Format | VistumblerMDB import / export | VistumblerMAUI import / export |
|---|---|---|
| VS1 / VSZ | ✓ / ✓ | ✅ / ✅ |
| Vistumbler CSV | ✓ / ✓ | ✅ / ✅ |
| WiGLE CSV | ✓ / ✓ | ✅ / ✅ |
| Kismet (.kismet, .netxml) | ✓ / ✓ | ✅ / ✅ |
| GPX | n/a / ✓ | n/a / ✅ |
| NS1 binary | ✓ / ✓ | 🟡 / ✅ import only reads format version 12 |
| NS1 text (wi-scan) | ✓ / ✓ | ❌ / ❌ the picker accepts `.txt`, but the file imports nothing |
| WarDrive DB3 | ✓ / n/a | ❌ the "Wardrive" option parses the file as CSV, not as the DB3 SQLite database |
| Import a whole folder | ✓ | ✅ |
| Export only the filtered APs | ✓ | ❌ |
| Export/import settings, Open Save Folder | ✓ | ❌ (the share sheet replaces Open Save Folder) |

## Sound

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Sound for a new AP | ✓ | ✅ once per scan, once per AP, or per AP by signal |
| Speak signal | ✓ | ✅ device voice or the original's recorded words |
| Error sound | ✓ | ✅ when the GPS receiver has a problem |
| Sound for a new GPS fix | ✓ | ❌ (the original never shipped new_gps.wav) |
| MIDI signal sounds | ✓ | ❌ |

## WifiDB

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Upload to WifiDB | ✓ experimental | ✅ plus import status, a retry queue and auto upload of saves |
| Auto upload live APs (WifiDB live sessions) | ✓ experimental | ❌ |
| Locate an AP in WifiDB | ✓ experimental | ✅ through WifiDB's search API, with a link to the AP's page |
| Find GPS position from WifiDB, update geolocations | ✓ experimental | ❌ |
| Open the live AP page, the WifiDB website, the PHP graph | ✓ | 🟡 Links menu opens the website and live page; no PHP graph |
| Account set up by QR code or registration link | n/a | ➕ |

## Everything else

| Feature | VistumblerMDB | VistumblerMAUI |
|---|---|---|
| Check for updates | ✓ | ✅ plus in-place install on Windows; off in the Google Play build |
| Language files | ✓ | ❌ English only |
| Help, forum, wiki and donate links | ✓ | ✅ Links menu (donate and store left out of the Google Play build) |
| Portable mode, download images, minimal GUI, batch list insert | ✓ experimental | ❌ |
| Native Wi-Fi vs netsh, search words, column widths | ✓ | n/a these were Windows-UI or netsh specific |
| Platforms | Windows | ➕ Windows, Android (on Google Play), and an iOS build |

## Gaps worth closing next

1. **NS1 text and WarDrive DB3 import:** offered on the Import page but they import nothing.
2. **Filters:** these also unlock filtered exports.
3. **Signal and range circle maps for a selected AP:** the one Google Earth feature the built-in map doesn't cover yet.
4. **Save all GPS data, even with no APs.**

Closed since this was first written: manufacturer lookup, sounds and speak signal, Import Folder, Locate in WifiDB,
GPS Details and Compass, Copy, coordinate formats, adapter choice, Auto Scan on launch, and the Help/WifiDB links.
