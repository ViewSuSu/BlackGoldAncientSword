# BlackGoldAncientSword — Naraka Bladepoint Stats Assistant

> A desktop companion app for querying *NARAKA: BLADEPOINT* player statistics and match data.

[![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?style=flat&logo=windows&logoColor=white)]() [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)]() [![PowerShell](https://img.shields.io/badge/PowerShell-UTF--8-5391FE?style=flat&logo=powershell&logoColor=white)]()
---

# User Guide

## Overview

**BlackGoldAncientSword** is a Windows desktop application that automatically detects game status, recognizes teammates via OCR, and fetches real-time player stats. No need to alt-tab to a browser — stats are displayed directly on your desktop. Supports **Solo / Duo / Trio** modes and **Ranked / Casual / Immortal** match types.

---

## Home — Game Status Monitor

The home page automatically detects whether *NARAKA: BLADEPOINT* is running. When the game is detected, it monitors the game log to track the current phase:

- **Waiting for game**: NARAKA is not running
- **Hero Selection**: Hero pick phase (RoomId displayed)
- **In Game**: Match in progress (BattleId displayed)
- **Battle Ended**: Match concluded

![Home screenshot](docs/images/01_home.png)

Use the left sidebar to switch between: **Home**, **Stats**, **Team Info**, and **Settings**. The title bar shows the version number and update indicator.

---

## Player Stats

Enter a player nickname in the search box to fetch full stats:

- **Season overview**: K/D, first-place rate, top-5 rate, avg kills/heals/assists/survival
- **Best records**: Most kills, heals, assists, damage, parries
- **Rank info**: Current season rank score and tier name (with star count for celestial tier)
- **Recent 10 battles**: Hero, mode, kills/damage, rank score with ± delta, honor titles

Filter by season, mode category (Ranked / Casual / Immortal), and team size (Trio / Duo / Solo).

![Stats screenshot](docs/images/02_stats.png)

---

## Team Info — OCR Recognition

When entering hero selection, the app uses **OCR to capture the screen and recognize teammate nicknames** automatically. Teammate stats are displayed side-by-side for quick assessment.

- Automatic teammate recognition (no manual input)
- Supports Trio / Duo / Solo teams
- Ranked / Casual / Immortal mode switching
- Side-by-side stat comparison
- Team info **locks** once the match starts

![Team Info screenshot](docs/images/03_team_info.png)

---

## Settings

- **Data path**: Local storage directory for stats (customizable, with auto-migration)
- **Cache path**: Image cache directory (size display + one-click clear)
- **Language**: 简体中文 / English / 繁體中文
- **Close behavior**: Minimize to tray or exit directly
- **Current version**

![Settings screenshot](docs/images/04_settings.png)

---

## Other Features

### System Tray

Minimize to system tray during gameplay. Right-click the tray icon to restore or exit.

![Close prompt screenshot](docs/images/05_close_prompt.png)

### Toast Notifications

Success/error toasts appear in the bottom-right corner (e.g. "Copied", "Cache cleared").

### Auto Updates

Online update feature has been removed.

---

<br>
<br>
<br>


# Developer Guide

## Architecture Overview

```
┌──────────────────────────────────────────┐
│          BlackGoldAncientSword.App       │  ← WPF entry point
│          (Shell / MainWindow)            │
└────────────────────┬─────────────────────┘
                     │
     ┌───────────────┼───────────────┐
     │               │               │
     ▼               ▼               ▼
┌─────────┐  ┌─────────────┐  ┌───────────┐
│ Modules │  │  Framework  │  │ Resources │
│ (8 UI   │  │  (Core +    │  │ (Strings, │
│  Pages) │  │   Services) │  │  Images)  │
└────┬────┘  └──────┬──────┘  └───────────┘
     │              │
     ▼              ▼
┌──────────┐  ┌──────────────┐
│GameMonitor│  │ScreenCapture │
│(Process/ │  │  (WGC API)   │
│ Log)     │  │              │
└─────┬─────┘  └──────┬───────┘
      │               │
      ▼               ▼
┌──────────┐  ┌──────────────┐
│   Ocr    │  │ PaddleOCR-   │
│ (Engine) │  │  json.exe    │
└──────────┘  └──────────────┘
```

### Layer Map

| Layer | Project | Responsibility |
|---|---|---|
| **Shell** | `BlackGoldAncientSword.App` | App entry, main window, navigation, tray, updates |
| **UI Modules** | `BlackGoldAncientSword.Modules` | 8 independent page modules, on-demand loading |
| **Core Framework** | `BlackGoldAncientSword.Framework` | MVVM base, Prism infra, HTTP API, localization, settings |
| **Game Monitor** | `BlackGoldAncientSword.GameMonitor` | Process detection, log parsing, state machine |
| **Screen Capture** | `BlackGoldAncientSword.ScreenCapture` | Windows Graphics Capture API via SharpDX |
| **OCR Engine** | `BlackGoldAncientSword.Ocr` | PaddleOCR-json wrapper |
| **Resources** | `BlackGoldAncientSword.Resources` | Multi-language XAML resource dictionaries, icons |
| **Source Gen** | `BlackGoldAncientSword.Framework.SourceGenerator` | Compile-time HTTP client generation from JSON |
| **Tests** | `BlackGoldAncientSword.Tests` | OCR, screen capture, game monitor, and update tests |

---

## Tech Stack

| Category | Technology / Library | Purpose |
|---|---|---|
| **Runtime** | .NET 10.0 (net10.0-windows) | Target framework |
| **UI** | WPF + HandyControl 3.5 | Desktop UI and control library |
| **MVVM** | Prism 8.1 (DryIoc) | DI container, region navigation, modularization |
| **HTTP** | Compile-time source generator | Auto-generate typed API clients from JSON definitions |
| **Mapping** | Mapster 7.4 | DTO ↔ ViewModel |
| **JSON** | Newtonsoft.Json 13 | Serialization / deserialization |
| **Screen Capture** | SharpDX + native WGC DLL (C++) | Game window capture |
| **OCR** | PaddleOCR-json.exe | Chinese character recognition |
| **System Tray** | Hardcodet.NotifyIcon.Wpf | Tray icon and context menu |
| **Packaging** | Self-Contained + PublishSingleFile | Single-file deployment (win-x64) |

---

## Project Structure

```
src/
├── BlackGoldAncientSword.App/              # WPF startup project
│   ├── App.xaml / App.xaml.cs              # App entry, Prism bootstrap
│   ├── Shell/
│   │   ├── MainWindow.xaml                 # Shell layout (sidebar + nav + tray)
│   │   └── MainWindowViewModel.cs          # Nav commands, game status, update detection
│   └── BlackGoldAncientSword.App.csproj
│
├── BlackGoldAncientSword.Framework/        # Core framework
│   ├── Core/
│   │   ├── Attributes/
│   │   │   └── ComponentAttribute.cs            # Custom component marker attribute
│   │   ├── Bases/ViewModels/ViewModelBase.cs  # MVVM base class (RaisePropertyChanged)
│   │   ├── Bases/Views/UserControlBase.cs      # View base class
│   │   ├── Consts/
│   │   │   ├── GlobalConstant.cs                # Global constants
│   │   │   └── PageNames.cs                     # Page name constants
│   │   ├── Events/                             # Prism EventAggregator events
│   │   │   ├── GameStatus.cs                   # Game status enum
│   │   │   ├── GameStatusChangedEventArgs.cs   # Status change event args
│   │   │   ├── SettingsChangedEvent.cs         # Settings change event
│   │   │   └── TipMessageEvent.cs              # Toast message event
│   │   ├── Extensions/                         # Extension methods
│   │   └── Infrastructure/                     # Navigation interfaces
│   │       ├── IMainContentNavigationService.cs  # Nav service interface
│   │       └── MainContentNavigator.cs          # Nav service implementation
│   ├── Http/
│   │   └── Definitions/                        # API JSON definitions → source gen
│   ├── Services/
│   │   ├── AppSettings.cs                       # App config data model
│   │   ├── LanguageOption.cs                    # Language option model
│   │   ├── SearchHistoryItem.cs                 # Search history model
│   │   ├── ServiceAutoRegister.cs              # Service auto-registration
│   │   ├── Abstractions/                       # Service interfaces (7)
│   │   └── Implementation/                     # Service implementations
│   ├── Themes/Generic.xaml                     # HandyControl theme
│   └── UI/Controls/
│       └── DataGridWrapPanel.cs                # DataGrid wrap panel
│
├── BlackGoldAncientSword.Modules/          # UI page modules
│   ├── Mappings/
│   │   └── BattleMappingRegister.cs          # Mapster mapping registration
│   ├── Module/                               # Prism IModule registrations (8)
│   └── UI/
│       ├── Announcement/                     # Announcement page
│       ├── ClosePrompt/                      # Close confirmation dialog
│       ├── Feedback/                         # Feedback page
│       ├── Home/                             # Home (game status monitor)
│       ├── Search/                           # Search history
│       ├── Settings/                         # Settings page
│       ├── Stats/                            # Player stats
│       └── TeamInfo/                         # Team info (OCR + comparison)
│
├── BlackGoldAncientSword.GameMonitor/      # Game monitoring
│   ├── GameMonitorAutoRegister.cs            # Service auto-registration
│   ├── GlobalUsing.cs                        # Global usings
│   ├── Models/                               # BattleEventArgs, PlayerPrefsData
│   └── Services/
│       ├── Abstractions/
│       │   ├── IGameLogMonitor.cs            # Log monitor interface
│       │   ├── IGameStatusMonitor.cs         # Status monitor interface
│       │   └── IPlayerPrefsService.cs        # Preferences service interface
│       └── Implementation/
│           ├── GameLogMonitor.cs             # Player.log parser
│           ├── GameStatusMonitor.cs          # Game state machine
│           └── PlayerPrefsService.cs         # Local user preferences
│
├── BlackGoldAncientSword.ScreenCapture/     # Screen capture
│   ├── GlobalUsing.cs                        # Global usings
│   ├── IScreenCaptureService.cs             # Capture service interface
│   ├── NativeWgc.cs                          # Native WGC API interop
│   ├── ScreenCaptureAutoRegister.cs          # Service auto-registration
│   ├── ScreenCaptureService.cs              # WGC wrapper
│   ├── ScreenQuadrant.cs                     # Screen quadrant split
│   ├── WgcInterop.cs                         # WGC COM interop wrapper
│   └── native/
│       └── wgc_capture.dll                  # Native C++ capture library
│
├── BlackGoldAncientSword.Ocr/               # OCR engine
│   ├── GlobalUsing.cs                        # Global usings
│   ├── IOcrService.cs                        # OCR service interface
│   ├── JobObjectHelper.cs                    # Child process lifecycle management
│   ├── OcrAutoRegister.cs                    # Service auto-registration
│   └── OcrEngine.cs                          # PaddleOCR-json.exe wrapper
│
├── BlackGoldAncientSword.Resources/         # Multi-language resources
│   ├── Images/                               # UI image resources
│   └── Themes/
│       ├── Strings.zh-CN.xaml               # Simplified Chinese
│       ├── Strings.en.xaml                  # English
│       └── Strings.zh-TW.xaml               # Traditional Chinese
│
├── BlackGoldAncientSword.Tests/             # Test project
│   ├── GameMonitor/                          # Game monitor tests
│   ├── Ocr/                                  # OCR tests
│   ├── ScreenCapture/                        # Screen capture tests
│   ├── TestData/                             # Test data
│   └── Update/                               # Update flow tests
│
└── ocr_engine/                               # PaddleOCR-json engine files
    ├── PaddleOCR-json.exe                    # OCR engine executable
    ├── models/                               # OCR model files
    └── *.dll                                 # Runtime dependencies (onnxruntime, OpenCV, etc.)
```

---

## Core Module Details

### 1. MVVM Architecture (Prism + DryIoc)

- All ViewModels inherit from `ViewModelBase` with `RaisePropertyChanged()` (no `SetProperty` wrapper per project conventions)
- Property change notifications use `nameof()` or `[CallerMemberName]`, string literals forbidden
- Navigation via `IMainContentNavigationService` with forward/back support
- Cross-module communication via `IEventAggregator` (e.g. `TipMessageEvent`)

### 2. On-Demand Module Loading

Each of the 8 UI pages is a Prism `IModule` registered as `OnDemand` in `ModuleCatalogConfigManager`. Modules are only loaded on first navigation, reducing startup time.

```csharp
// PageNames.cs
public static class PageNames
{
    public const string HomePage     = nameof(HomePage);
    public const string StatsPage    = nameof(StatsPage);
    public const string SearchPage   = nameof(SearchPage);
    public const string TeamInfoPage = nameof(TeamInfoPage);
    public const string SettingsPage = nameof(SettingsPage);
    public const string AnnouncementPage = nameof(AnnouncementPage);
    public const string ClosePromptPage  = nameof(ClosePromptPage);
    public const string FeedbackPage     = nameof(FeedbackPage);
}
```

### 3. Game Status Monitoring (GameMonitor)

`GameLogMonitor` polls `Player.log` at intervals to detect game events:

- `BattleJoined` — hero selection (parses RoomId from log)
- `BattleStarted` — match start (parses BattleId)
- `BattleEnded` — match end

`GameStatusMonitor` maintains a state machine, notifying pages of the current phase. `HomePageViewModel` additionally uses `Process.GetProcessesByName("NarakaBladepoint")` as a secondary check.

### 4. Screen Capture & OCR (Team Info)

1. `GameStatusMonitor` detects `HeroSelection` state
2. `TeamInfoPageViewModel` starts OCR polling loop
3. `ScreenCaptureService` captures game window via **Windows Graphics Capture API** (native C++ DLL → SharpDX D3D11)
4. `OcrService` spawns **PaddleOCR-json.exe** to recognize text
5. `TeamInfoOcrService` parses OCR output and extracts teammate nicknames
6. Stats API is queried for each teammate, displayed side-by-side

### 5. Source-Generated HTTP Client

API clients are **not hand-written**. `BlackGoldAncientSword.Framework.SourceGenerator` reads JSON definitions from `Http/Definitions/*.json` at compile time and generates strongly-typed HTTP client code.

### 6. Localization

Multi-language support via WPF `ResourceDictionary`. All UI text is defined in `Strings.xx.xaml`. `ILocalizationService.ApplyLanguage()` dynamically swaps resource dictionaries at runtime — no restart needed.

---


## Build & Run

### Prerequisites

- Windows 10/11 x64
- .NET 10.0 SDK

### Build

```powershell
# Restore
dotnet restore src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj

# Debug build
dotnet build src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Debug

# Release publish (single-file exe)
dotnet publish src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Release -o publish/
```

### Run Tests

```powershell
dotnet test src/BlackGoldAncientSword.Tests/BlackGoldAncientSword.Tests.csproj

MIT License. Author: **小窗同学** (XiaoChuang).
