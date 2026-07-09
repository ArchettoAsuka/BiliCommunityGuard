# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BiliCommunityGuard is a WPF desktop application (.NET 10, Windows-only) that automatically scans Bilibili video and dynamic comment sections for comments from blacklisted users, then reports those comments using multiple Bilibili accounts.

## Build & Run

```bash
# Build
dotnet build

# Build release
dotnet build -c Release

# Publish self-contained single-file exe for win-x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Run (requires Windows with WPF support)
dotnet run
```

There are no automated tests in this project.

## Architecture

The app follows MVVM pattern with a single window.

### Data flow

1. **`MainViewModel`** loads three input files at startup (and on demand):
   - `config.yaml` — scan settings, UP UID list, report config
   - `cookie.json` — array of Bilibili cookie strings, one per account
   - `blacklist.txt` — one UID per line

2. On Start, `MainViewModel` creates a **`GuardBotRunner`** and runs it on a background `Task`. The runner owns all service instances for the lifetime of a run.

3. **`GuardBotRunner`** is the main scan loop:
   - Calls `ContentFetcher` to get the list of videos/dynamics to protect (based on `ProtectUps`, `VideoWindowSize`, `DynamicWindowSize`)
   - Iterates through content in batches (`ScanBatchSizePerCycle`), calling `CommentScanner` per item
   - Checks each comment's `AuthorMid` against the `HashSet<long>` blacklist
   - On a hit, calls `Reporter` using each available account in round-robin order
   - Uses `StateStore` to persist a cursor and report history across restarts

4. **`AccountManager`** manages `AccountSession` objects (one per cookie). It validates each session against the Bilibili nav API on startup, and tracks cooldown/invalid state per account. Fetch and report roles are separate — the first valid account fetches content; all valid non-cooling accounts attempt reports.

5. **`BiliApiClient`** is a thin HTTP wrapper. Each `AccountSession` owns its own `HttpClient`. `RequestDelayer` inserts a random delay (min/max from config) before every request. `WbiSigner` handles Bilibili's WBI request signing for endpoints that require it.

### Runtime state persistence

- `data/state.json` — content cursor and last-seen timestamps (relative to the exe directory)
- `data/report-history.json` — per-account report attempts per comment key, used to enforce `MinReReportInterval`

### Config format

`ConfigLoader` is a hand-rolled YAML parser (no external YAML library). It supports both Chinese and English key names (e.g., `扫描间隔秒数` and `scan_interval_seconds` are equivalent). `DryRun: true` makes the bot log without actually sending report requests.

### Infrastructure

- `ObservableObject` — base class implementing `INotifyPropertyChanged` with `SetProperty`
- `RelayCommand` — `ICommand` implementation used for all UI commands
- `JsonElementExtensions` — extension helpers for navigating `System.Text.Json` results (e.g., `TryGetInt32ByPath`, `TryGetStringByPath`)

## Key files

| File | Role |
|---|---|
| `ViewModels/MainViewModel.cs` | All UI state and command logic |
| `Services/GuardBotRunner.cs` | Main scan/report loop |
| `Services/AccountManager.cs` | Cookie session lifecycle |
| `Services/ContentFetcher.cs` | Fetches video/dynamic lists per UP |
| `Services/CommentScanner.cs` | Fetches comments for one content item |
| `Services/Reporter.cs` | Sends report API call |
| `Services/BiliApiClient.cs` | HTTP client factory and request helpers |
| `Services/WbiSigner.cs` | WBI signing for Bilibili API |
| `Services/StateStore.cs` | Load/save state and report history JSON |
| `Services/ConfigLoader.cs` | Hand-rolled YAML parser |
| `Models/AppConfig.cs` | All config fields with defaults |

## Runtime file locations

All runtime files are resolved relative to `AppDomain.CurrentDomain.BaseDirectory` (the exe directory):
- `config.yaml`, `cookie.json`, `blacklist.txt` — default input file locations (browseable via UI)
- `data/state.json`, `data/report-history.json` — always written here, not configurable
