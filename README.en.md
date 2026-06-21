[中文](README.md) | [English](README.en.md)

[![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?style=flat&logo=windows&logoColor=white)]() [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)]()

# BlackGoldAncientSword — Naraka Bladepoint Stats Assistant

> A desktop companion app for querying *NARAKA: BLADEPOINT* player statistics and match data.

> This project was inspired by [Zzaphkiel/Seraphine](https://github.com/Zzaphkiel/Seraphine). Thanks to the pioneers for their contributions.

---

## Download 📥

[![Download](https://img.shields.io/badge/Download-Latest%20Release-blue?style=flat&logo=github)](https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest/download/BlackGoldAncientSword-Setup.exe)

Click the button above to directly download the latest .exe package.

# User Guide

## Overview

**BlackGoldAncientSword** is a Windows desktop application that automatically detects game status, recognizes teammates, and fetches real-time player stats. No need to alt-tab to a browser — stats are displayed directly on your desktop. Supports **Solo / Duo / Trio** modes and **Ranked / Casual / Immortal** match types.

## Player Stats

Enter a player nickname in the search box to fetch full stats:

- **Season overview**: K/D, first-place rate, top-5 rate, avg kills/heals/assists/survival
- **Best records**: Most kills, heals, assists, damage, parries
- **Rank info**: Current season rank score and tier name (with star count for celestial tier)
- **Recent 10 battles**: Hero, mode, kills/damage, rank score with ± delta, honor titles

Filter by season, mode category (Ranked / Casual / Immortal), and team size (Trio / Duo / Solo).

![Stats screenshot](docs/images/02_stats.png)

---

## Team Info — Smart Recognition

When entering hero selection, the app captures the screen and recognizes teammate nicknames automatically. Teammate stats are displayed side-by-side for quick assessment.

- Automatic teammate recognition (no manual input)
- Supports Trio / Duo / Solo teams
- Ranked / Casual / Immortal mode switching
- Side-by-side stat comparison
- Team info **locks** once the match starts

<p align="center">
  <img src="docs/images/03_team_info.png" width="49%">
  <img src="docs/images/03_team_info_recognition.png" width="49%">
</p>

> Click "Re-identify Team" anytime to trigger a manual refresh. If some names are recognized incorrectly, you can edit them directly and re-query.

---

## Settings

- **Data path**: Local storage directory for stats (customizable, with auto-migration)
- **Cache path**: Image cache directory (size display + one-click clear)
- **Language**: 简体中文 / English / 繁體中文
- **Close behavior**: Default action when clicking the close button — choose from *Ask every time / Minimize to taskbar / Minimize to system tray / Exit directly*, with a "remember choice" option
- **Team overlay during hero selection**: Toggle the bottom-right teammate popup
- **Check for updates**: Manually check and download new releases
- **Current version**

![Settings screenshot](docs/images/04_settings.png)

---

## Other Features

### System Tray

Minimize to system tray during gameplay. Right-click the tray icon to restore or exit.

![Close prompt screenshot](docs/images/05_close_prompt.png)

---

## FAQ 🧐

**Q: Will I get banned for using BlackGoldAncientSword? 😨**

This app only reads the game log file (Player.log) and captures the hero selection screen for OCR recognition. It does not modify or inject into game files or memory in any way. You are very unlikely to be banned, though no guarantee can be made.

**Q: Why can't I query stats / why is data delayed?**

All stats data comes from the same API powering https://naraka.drivod.top/ , provided by craftwyrd. The app only displays the data. If data is unavailable or delayed, the issue is almost certainly on the API server side. For data-related issues, ask in the data feedback QQ group or contact the API author craftwyrd directly.

**Q: Why does teammate recognition fail or show inaccurate results?**

OCR recognition uses the same underlying technology as OBS screen capture, capturing directly from the graphics card layer and bypassing any overlays. However, it currently only supports recognition when the screen and game resolutions match. If your screen resolution differs from the game resolution, black bars will appear on the sides — please avoid playing in such a setup. For best results, play in fullscreen at the highest resolution or at a resolution matching your display.

Additionally, OCR may sometimes fail to recognize certain special characters. If you encounter unrecognized names, you can use QQ screenshot text recognition or similar tools as a manual workaround.

**Q: Why is the installer / program so large (200MB+)?**

The app is published as a **self-contained** deployment, which bundles the .NET runtime so users can run it without installing .NET separately. Additionally, the built-in OCR engine (PaddleOCR) depends on the Intel Math Kernel Library (mklml.dll, ~88MB) and the OpenCV computer vision library (opencv_world4100.dll, ~62MB). These two native AI/vision libraries, together with the .NET runtime, account for the vast majority of the download size. Without them, automatic screenshot recognition of teammate names wouldn't be possible — the "bulk" is a necessary price to pay 😅.

**Q: Why do I see a yellow border around my screen when entering hero selection?**

The yellow border is a visual indicator that the app is capturing the screen and recognizing teammate information. This is normal behavior — no need to worry.

---

## Disclaimer 📢

BlackGoldAncientSword is not endorsed by 24 Entertainment or NetEase and does not reflect the views or opinions of 24 Entertainment, NetEase, or anyone officially involved in producing or managing NARAKA: BLADEPOINT. NARAKA: BLADEPOINT and all associated properties are trademarks or registered trademarks of 24 Entertainment / NetEase.

---

## Legal Shield 🛡️

This program is open-sourced at [ViewSuSu/BlackGoldAncientSword](https://github.com/ViewSuSu/BlackGoldAncientSword) with binaries distributed via GitHub Releases and official QQ groups. This section aims to help users fully understand the program and its potential risks, enabling informed decisions before and during use.

The purpose of this program is to provide out-of-game auxiliary features (stats querying, teammate recognition, etc.) that enhance the player experience. We do not encourage or support any behavior that violates 24 Entertainment or NetEase policies or that may lead to an unfair gaming environment.

This program achieves its functionality by reading the game log file (Player.log) and performing OCR on screen captures. Its code and behavior contain no intrusive measures whatsoever; it does not modify client files or read/write game process memory, and should not compromise the integrity of the game client in any way.

We strive to ensure the stability of both the program and the game client during use. However, changes to the game environment or official services (such as anti-cheat system updates) may negatively impact your gaming experience, including client crashes or account bans.

You assume all consequences arising from the use of this program. We are not liable for any direct or indirect damages resulting from its use. By deciding to use this program, you fully acknowledge and accept all associated risks and consequences.

We reserve the right to modify this disclaimer at any time. Please check this page regularly for the latest information.

Before using this program, please ensure you have read, understood, and agreed to the terms of this disclaimer. Please also abide by the relevant game rules and help maintain a healthy and fair gaming environment.


## Star Us on GitHub ⭐

[![Star History Chart](https://api.star-history.com/svg?repos=ViewSuSu/BlackGoldAncientSword&type=Date)](https://star-history.com/#ViewSuSu/BlackGoldAncientSword&Date)

## Feedback & Community

- **App Feedback QQ Group**:
  - Group ①: 146088141
- **Data Feedback QQ Groups** (QQ group bot also available for stats queries):
  - Group ①: 476074617
  - Group ②: 649891198
  - Group ③: 966720321
  - QQ level 32+ (two suns) required for auto-approval; low-level accounts will be rejected
- **Web Version**: https://naraka.drivod.top/

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
| **JSON** | System.Text.Json (with source-generated context) | Serialization / deserialization (fully replaced Newtonsoft.Json) |
| **Screen Capture** | SharpDX + native WGC DLL (C++) | Game window capture |
| **OCR** | PaddleOCR-json.exe (long-lived child process) | Multi-language text recognition |
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
│   │   ├── Bases/
│   │   │   ├── PrismApplicationBase.cs        # Prism application base class
│   │   │   ├── ViewModels/ViewModelBase.cs    # MVVM base class (RaisePropertyChanged)
│   │   │   └── Views/UserControlBase.cs       # View base class
│   │   ├── Consts/
│   │   │   ├── GlobalConstant.cs                # Global constants
│   │   │   └── PageNames.cs                     # Page name constants
│   │   ├── Events/                             # Prism EventAggregator events
│   │   │   ├── GameStatus.cs                   # Game status enum
│   │   │   ├── GameStatusChangedEventArgs.cs   # Status change event args
│   │   │   ├── SettingsChangedEvent.cs         # Settings change event
│   │   │   └── TipMessageEvent.cs              # Toast message event
│   │   ├── Extensions/                         # Extension methods & value converters
│   │   └── Infrastructure/                     # Navigation interfaces
│   │       ├── IMainContentNavigationService.cs  # Nav service interface
│   │       └── MainContentNavigator.cs          # Nav service implementation
│   ├── Http/
│   │   ├── Definitions/                        # API JSON definitions → source gen
│   │   └── JsonFlexibleStringConverter.cs      # System.Text.Json fault-tolerant converter
│   ├── Services/
│   │   ├── AppSettings.cs                       # App config data model
│   │   ├── LanguageOption.cs                    # Language option model
│   │   ├── SearchHistoryItem.cs                 # Search history model
│   │   ├── ServiceAutoRegister.cs              # Service auto-registration
│   │   ├── Abstractions/                       # Service interfaces (12)
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
│           └── Services/                     # Contains TeamInfoOcrService, TeamOcrCoordinator
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
│   ├── Http/                                 # HTTP / JSON fault-tolerance tests
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

`GameLogMonitor` watches `Player.log` using **`FileSystemWatcher` plus a polling fallback**. Internally the responsibilities are split into `LogReader` (file IO), `LogPoller` (poll loop) and `BattleStateMachine` (battle state); the monitor itself is just a façade that orchestrates lifetime and event dispatch. Detected events:

- `BattleJoined` — hero selection (parses RoomId from log)
- `BattleStarted` — match start (parses BattleId)
- `BattleEnded` — match end

`GameStatusMonitor` maintains a state machine, notifying pages of the current phase. `HomePageViewModel` additionally uses `Process.GetProcessesByName("NarakaBladepoint")` as a secondary check.

### 4. Screen Capture & OCR (Team Info)

1. `GameStatusMonitor` detects `HeroSelection` state
2. `TeamInfoPageViewModel` starts OCR polling loop
3. `ScreenCaptureService` captures the game window via **Windows Graphics Capture API** (native C++ DLL → SharpDX D3D11), reusing full-frame buffers from `ArrayPool` and slicing three regions via `ScreenQuadrant` into a single composite image
4. `OcrEngine` talks to **PaddleOCR-json.exe** as a **singleton long-lived child process** over stdin/stdout pipes using `image_base64` (zero disk IO). Models load once on the first `PrewarmAsync` call (~600–1500 ms); subsequent calls only run inference (~100–250 ms). A `JobObject` guarantees the child is reaped by the OS if the host crashes
5. `TeamInfoOcrService` / `TeamOcrCoordinator` parse the OCR output and extract teammate nicknames
6. The stats API is queried for each teammate and displayed side-by-side

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
```

```

---

### Special Thanks

- WeChat: craftwyrd

---

## License

MIT License. Author: **小窗同学** (XiaoChuang).
