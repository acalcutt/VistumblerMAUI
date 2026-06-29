# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.2.1
### ✨ Features and improvements

### 🐞 Bug fixes

## 0.2.0
### ✨ Features and improvements
- **Merged the Scan and APs tabs into one** — both tabs rendered essentially the same AP list at different times (live during a scan, persisted between scans), which is redundant. The surviving "Scan" tab now loads persisted APs from the database on first appearance (so the list isn't empty before you tap Start), keeps the search bar and Clear All action from the former APs tab, and still supports the Map page's "View in AP List" BSSID deep-link (now routed to `//ScanPage` instead of the removed `//AccessPointListPage`).

### 🐞 Bug fixes
