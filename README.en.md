[中文](README.md) | [English](README.en.md)

[![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?style=flat&logo=windows&logoColor=white)]() [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)]() [![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20Prism%208.1-purple?style=flat)]() [![License](https://img.shields.io/badge/License-MIT-green?style=flat)](LICENSE)

# BlackGoldAncientSword — Naraka Bladepoint Stats Assistant

> A desktop companion app for querying *NARAKA: BLADEPOINT* player statistics and match data.

> This project was inspired by [Zzaphkiel/Seraphine](https://github.com/Zzaphkiel/Seraphine). Thanks to the pioneers for their contributions.

## Project Introduction Video 🎬

[![Bilibili](https://img.shields.io/badge/Project%20Intro-Bilibili-00A1D6?style=for-the-badge&logo=bilibili&logoColor=white)](https://www.bilibili.com/video/BV1WU7p6SE3T/)

Click the button above to watch the project introduction video.

---

## Download 📥

[![Download](https://img.shields.io/badge/Download-Latest%20Release-blue?style=flat&logo=github)](https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest)

Click the button above to directly download the latest .exe installer.

# User Guide

## Overview

**BlackGoldAncientSword** is a Windows desktop application that automatically detects game status, recognizes teammates, and fetches real-time player stats. No need to alt-tab to a browser — stats are displayed directly on your desktop. Supports **Solo / Duo / Trio** team sizes and **Ranked / Casual / Immortal** match types.

## Player Stats

Enter a player nickname in the search box to fetch full stats:

- **Season overview**: K/D, first-place rate, top-5 rate, avg kills/heals/assists/survival
- **Best records**: Most kills, heals, assists, damage, parries
- **Rank info**: Current season rank score and tier name (with star count for celestial tier)
- **Recent 10 battles**: Hero, mode, kills/damage, rank score with ± delta, honor titles

Filter by season, mode category (Ranked / Casual / Immortal), and team size (Trio / Duo / Solo).

<p align="center">
  <img src="docs/images/02_stats.png" alt="Stats screenshot" /><br />
  <small><u>Stats</u></small>
</p>

> Click the copy button next to a player's nickname to quickly copy the nickname or UID.

---

## Team Info — Smart Recognition

When entering hero selection, the app captures the screen and recognizes teammate nicknames automatically. Teammate stats are displayed side-by-side for quick assessment.

- Automatic teammate recognition (no manual input)
- Supports Trio / Duo / Solo teams
- Ranked / Casual / Immortal mode switching
- Side-by-side stat comparison
- Team info **locks** once the match starts

<p align="center">
  <img src="docs/images/03_team_info.png" alt="Team Info screenshot" /><br />
  <small><u>Team Info Recognition 1</u></small>
</p>

<p align="center">
  <img src="docs/images/03_team_info_recognition.png" alt="Team Info OCR recognition result" /><br />
  <small><u>Team Info Recognition 2</u></small>
</p>

> Click "Re-identify Team" anytime to trigger a manual refresh. If some names are recognized incorrectly, you can edit them directly and re-query.

---

## Settings

- **Data path**: Local storage directory for stats (customizable, with auto-migration)
- **Cache path**: Image cache directory (size display + one-click clear)
- **Language**: 简体中文 / English / 繁體中文
- **Close behavior**: Default action when clicking the close button — *Ask every time / Minimize to taskbar / Minimize to system tray / Exit directly*, with a "remember choice" option
- **Team overlay during hero selection**: Toggle the bottom-right teammate popup
- **Check for updates**: Manually check and download new releases (delegates to the standalone Updater process, see below)
- **Current version**

<p align="center">
  <img src="docs/images/04_settings.png" alt="Settings screenshot" /><br />
  <small><u>Settings</u></small>
</p>

---

## Online Update

The app compares the latest GitHub Release version both at startup and via "Settings → Check for updates". When a new version is detected, an update notification page is shown. Click "Update Online" to:

1. The main app launches the standalone **BlackGoldAncientSword.Update.exe** (the updater) with the download URL, install directory, and main-app file name as arguments;
2. The updater downloads the new zip → extracts → overlays the install directory → relaunches the main app → exits itself.

> The updater is fully decoupled from the main app (no references to App / Framework / Modules), so no DLL is locked during the overlay step.

---

## Other Features

### System Tray

Minimize to system tray during gameplay. Right-click the tray icon to restore or exit.

<p align="center">
  <img src="docs/images/05_close_prompt.png" alt="Close prompt screenshot" /><br />
  <small><u>Close Prompt</u></small>
</p>

- Tray icon reflects online status
- Confirm dialog on close: "exiting will stop game detection"

---

## FAQ 🧐

**Q: Will I get banned for using BlackGoldAncientSword? 😨**

This app only reads the game log file (Player.log) and captures the hero selection screen for OCR. It does not modify or inject into game files or memory in any way. You are very unlikely to be banned, though no guarantee can be made.

**Q: Why can't I query stats / why is data delayed?**

All stats data comes from the same API powering https://naraka.drivod.top/ , provided by craftwyrd. The app only displays the data. If data is unavailable or delayed, the issue is almost certainly on the API server side. Ask in the data feedback QQ group or contact the API author craftwyrd directly.

**Q: Why does teammate recognition fail or show inaccurate results?**

OCR recognition uses the same underlying technology as OBS screen capture, capturing directly from the graphics card layer and bypassing any overlays. However, it currently only supports recognition when the screen and game resolutions match. If your screen resolution differs from the game resolution, black bars will appear on the sides — please avoid playing in such a setup. For best results, play in fullscreen at the highest resolution or at a resolution matching your display.

OCR may also fail on certain special characters. When that happens, use QQ screenshot OCR or similar tools as a manual workaround.

**Q: Why is the installer / program so large (200MB+)?**

The app is published as a **self-contained** deployment, which bundles the .NET runtime so users can run it without installing .NET separately. The built-in OCR engine (PaddleOCR) also depends on the Intel Math Kernel Library (mklml.dll, ~88MB) and the OpenCV computer vision library (opencv_world4100.dll, ~62MB). These two native AI/vision libraries, together with the .NET runtime, account for the vast majority of the download size. Without them, automatic OCR of teammate nicknames would not be possible — the "bulk" is a necessary price 😅.

**Q: Why do I see a yellow border around my screen when entering hero selection?**

The yellow border is a visual indicator that the app is capturing the screen and recognizing teammate information. This is normal behavior — no need to worry.

**Q: What if my antivirus flags the program?**

Because this program is not code-signed, it may be detected as a virus or suspicious file by antivirus software such as 360. You can temporarily disable the antivirus and then reopen the program.

**Q: What if the online update fails?**

The updater (BlackGoldAncientSword.Update.exe) runs as an independent process. Typical failure causes: no GitHub network access, insufficient permissions on the install directory, antivirus blocking the overlay. As a fallback, download the installer directly from the Releases page and reinstall over the existing install.

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

## Solution Overview

`src/BlackGoldAncientSword.slnx` contains **10 projects**: 8 class libraries + 2 executables (the main App and the standalone Updater).

```
┌────────────────────────────────────────────────────────┐
│             BlackGoldAncientSword.App                  │  ← WPF main entry (WinExe)
│             (Shell / MainWindow / Tray)                │
└──────────┬─────────────────────────────────────────┬───┘
           │ launches external process               │
           ▼                                          │
┌──────────────────────────┐                          │
│ BlackGoldAncientSword.   │                          │
│ Update (standalone WinExe)│                         │
│ Download/Extract/Overlay  │                         │
└──────────────────────────┘                          │
                                                      │
        ┌─────────────────────┬──────────────────────┘
        │                     │
        ▼                     ▼                     ▼
┌────────────┐        ┌──────────────┐        ┌───────────┐
│  Modules   │        │  Framework   │        │ Resources │
│ (9 UI page │ ◄────► │ (Core + 13   │ ◄──────│ (i18n XAML│
│  modules)  │        │  service IF) │        │  + icons) │
└─────┬──────┘        └──────┬───────┘        └───────────┘
      │                      │
      │             ┌────────┴───────┐
      ▼             ▼                ▼
┌──────────────┐ ┌──────────────┐ ┌────────────────────┐
│ GameMonitor  │ │ ScreenCapture│ │ Framework.         │
│ (process/log │ │ (WGC API +   │ │ SourceGenerator    │
│ state machine│ │ native DLL)  │ │ (compile-time HTTP)│
└──────┬───────┘ └──────┬───────┘ └────────────────────┘
       │                │
       ▼                ▼
┌──────────────┐ ┌──────────────────┐
│     Ocr      │ │ PaddleOCR-json   │
│ (subprocess) │ │  .exe + models   │
└──────────────┘ └──────────────────┘
```

### Layer Map

| Layer | Project | Output | Responsibility |
|---|---|---|---|
| **Main App** | `BlackGoldAncientSword.App` | WinExe | App entry, main window, sidebar nav, tray, launches Updater |
| **Updater** | `BlackGoldAncientSword.Update` | WinExe | Standalone online-update process, zero business deps (HandyControl only) |
| **UI Modules** | `BlackGoldAncientSword.Modules` | ClassLib | 9 Prism `IModule` pages, on-demand loading |
| **Core Framework** | `BlackGoldAncientSword.Framework` | ClassLib | MVVM base, Prism infra, service abstractions/implementations, HTTP API |
| **Game Monitor** | `BlackGoldAncientSword.GameMonitor` | ClassLib | Process detection, Player.log parsing, battle state machine |
| **Screen Capture** | `BlackGoldAncientSword.ScreenCapture` | ClassLib | Windows Graphics Capture API + SharpDX + native `wgc_capture.dll` |
| **OCR Engine** | `BlackGoldAncientSword.Ocr` | ClassLib | PaddleOCR-json.exe subprocess wrapper |
| **Resources** | `BlackGoldAncientSword.Resources` | ClassLib | Multi-language XAML resource dictionaries, icons, images |
| **Source Gen** | `BlackGoldAncientSword.Framework.SourceGenerator` | Roslyn Analyzer | Compile-time HTTP client + test code generation from JSON |
| **Tests** | `BlackGoldAncientSword.Tests` | xUnit | OCR, screen capture, game monitor, HTTP, update tests |

---

## Tech Stack

| Category | Technology / Library | Purpose |
|---|---|---|
| **Runtime** | .NET 10.0 (`net10.0-windows`) | Target framework |
| **UI** | WPF + HandyControl 3.5 | Desktop UI and control library |
| **MVVM** | Prism 8.1 (`Prism.DryIoc`) | DI container, region navigation, modularization |
| **HTTP** | Compile-time source generator | Generate strongly-typed API clients from `api-definitions.json` |
| **Mapping** | Mapster 7.4 | DTO ↔ ViewModel |
| **JSON** | `System.Text.Json` (with source-generated context) | Serialization / deserialization (fully replaced Newtonsoft.Json) |
| **Screen Capture** | SharpDX.Direct3D11 + native WGC DLL (C++/WinRT) | Game window capture |
| **OCR** | PaddleOCR-json.exe (long-lived child process) | Multi-language text recognition |
| **System Tray** | Hardcodet.NotifyIcon.Wpf | Tray icon and context menu |
| **Tests** | xUnit + Moq | Unit and integration tests |
| **Packaging** | Self-Contained + PublishSingleFile | Both App and Updater shipped as single-file .exe (win-x64) |
| **Installer** | Inno Setup | Produces `BlackGoldAncientSword-{version}-win-x64-Setup.exe` |

---

## Project Structure

```
src/
├── BlackGoldAncientSword.App/              # WPF main entry (WinExe)
│   ├── App.xaml / App.xaml.cs              # App entry, Prism bootstrap
│   └── Shell/
│       ├── MainWindow.xaml(.cs)            # Shell (sidebar + nav + tray)
│       └── MainWindowViewModel.cs          # Nav commands, game status, update detection
│
├── BlackGoldAncientSword.Update/           # Standalone online updater (WinExe, zero business deps)
│   ├── App.xaml(.cs)                       # Entry: parses --url / --target / --main-exe
│   ├── Services/
│   │   ├── UpdateOptions.cs                # Command-line argument model
│   │   └── UpdaterRunner.cs                # Orchestrates: download → extract → close main → overlay → relaunch
│   ├── Shell/UpdateWindow.xaml(.cs)        # Progress window
│   └── ViewModels/UpdateViewModel.cs       # Progress & status bindings
│
├── BlackGoldAncientSword.Framework/        # Core framework
│   ├── Core/
│   │   ├── Attributes/                     # ComponentAttribute (auto-DI marker)
│   │   ├── Bases/                          # ViewModelBase, PrismApplicationBase, etc.
│   │   ├── Consts/                         # GlobalConstant, PageNames
│   │   ├── Events/                         # GameStatusChanged, SettingsChanged, TipMessageEvent
│   │   ├── Extensions/                     # Extension methods & value converters
│   │   └── Infrastructure/                 # IMainContentNavigationService / MainContentNavigator
│   ├── Http/
│   │   ├── Definitions/
│   │   │   ├── api-definitions.json        # API endpoints / requests / responses (→ source gen)
│   │   │   └── enums.json                  # Enum definitions
│   │   └── JsonFlexibleStringConverter.cs  # Fault-tolerant System.Text.Json converter
│   ├── Services/
│   │   ├── Abstractions/                   # 13 service interfaces (see table below)
│   │   └── Implementation/                 # Service implementations
│   ├── Themes/Generic.xaml                 # HandyControl theme
│   └── UI/Controls/                        # Custom WPF controls (DataGridWrapPanel, etc.)
│
├── BlackGoldAncientSword.Framework.SourceGenerator/  # Roslyn source generator
│   ├── ApiDefinitionsParser.cs             # Parses api-definitions.json
│   ├── EnumSourceGenerator.cs              # Generates enum types
│   ├── HttpApiSourceGenerator.cs           # Generates NarakaApiClient + DTOs (Client mode)
│   └── HttpApiTestSourceGenerator.cs       # Generates HTTP API test code (Tests mode)
│
├── BlackGoldAncientSword.Modules/          # UI page modules (9 Prism IModule)
│   ├── Mappings/BattleMappingRegister.cs   # Mapster mapping registration
│   ├── Module/                             # 9 IModule registrations
│   │   ├── AnnouncementModule.cs           # Announcements
│   │   ├── ClosePromptModule.cs            # Close confirmation dialog
│   │   ├── FeedbackModule.cs               # Feedback
│   │   ├── HomeModule.cs                   # Home (game status monitor)
│   │   ├── SearchModule.cs                 # Search history
│   │   ├── SettingsModule.cs               # Settings
│   │   ├── StatsModule.cs                  # Player stats
│   │   ├── TeamInfoModule.cs               # Team info (OCR + comparison)
│   │   └── UpdateNotificationModule.cs     # New version prompt / launch Updater
│   └── UI/                                 # ViewModels + Views per module
│       ├── Stats/Services/                 # Stats aggregation services
│       ├── TeamInfo/Services/              # TeamInfoOcrService, TeamOcrCoordinator
│       └── UpdateNotification/ViewModels/  # Launches BlackGoldAncientSword.Update.exe
│
├── BlackGoldAncientSword.GameMonitor/      # Game monitoring
│   ├── Models/                             # BattleEventArgs, PlayerPrefsData
│   ├── Services/
│   │   ├── Abstractions/                   # IGameLogMonitor / IGameStatusMonitor / IPlayerPrefsService
│   │   └── Implementation/
│   │       ├── GameLogMonitor.cs           # Façade (orchestrates lifetime + event dispatch)
│   │       ├── GameStatusMonitor.cs        # Game state machine
│   │       ├── PlayerPrefsService.cs       # Local user preferences
│   │       └── Internal/
│   │           ├── BattleStateMachine.cs   # Battle state machine
│   │           ├── LogPoller.cs            # Polling loop
│   │           └── LogReader.cs            # Player.log reader
│   └── GameMonitorAutoRegister.cs          # Service auto-registration
│
├── BlackGoldAncientSword.ScreenCapture/    # Screen capture (Windows Graphics Capture API)
│   ├── IScreenCaptureService.cs            # Service interface
│   ├── ScreenCaptureService.cs             # WGC wrapper
│   ├── NativeWgc.cs / WgcInterop.cs        # Native WGC API interop
│   ├── ScreenQuadrant.cs                   # Screen quadrant split
│   ├── native/                             # Native C++ source + build script
│   └── runtimes/win-x64/native/
│       └── wgc_capture.dll                 # Native C++/WinRT capture library
│
├── BlackGoldAncientSword.Ocr/              # OCR engine
│   ├── IOcrService.cs                      # Service interface
│   ├── OcrEngine.cs                        # PaddleOCR-json.exe wrapper
│   ├── JobObjectHelper.cs                  # JobObject fallback for child-process cleanup
│   └── OcrAutoRegister.cs                  # Service auto-registration
│
├── BlackGoldAncientSword.Resources/        # Multi-language resources
│   ├── Images/                             # UI images, app icon (app.ico)
│   └── Themes/
│       ├── Strings.zh-CN.xaml              # Simplified Chinese
│       ├── Strings.en.xaml                 # English
│       └── Strings.zh-TW.xaml              # Traditional Chinese
│
└── BlackGoldAncientSword.Tests/            # Test project (xUnit + Moq)
    ├── GameMonitor/                        # Game monitor tests
    ├── Http/                               # HTTP / JSON fault-tolerance tests (some code source-generated)
    ├── Ocr/                                # OCR tests
    ├── ScreenCapture/                      # Screen capture tests
    ├── Update/                             # Update flow tests
    └── TestData/                           # Test data

ocr_engine/                                 # PaddleOCR-json engine + models (copied to output by Ocr)
├── PaddleOCR-json.exe                      # OCR engine executable
├── models/                                 # OCR models (ch / cht / en / cyrillic / japan / korean)
└── *.dll                                   # Runtime deps (onnxruntime, opencv_world, mklml, etc.)
```

---

## Framework Service Interfaces

`BlackGoldAncientSword.Framework/Services/Abstractions/` exposes 13 public interfaces:

| Interface | Main Implementation | Purpose |
|---|---|---|
| `IAppAssemblyMarker` | `AppAssemblyMarker` | Assembly locator marker (for XAML resource resolution) |
| `IApplicationLifetime` | `WpfApplicationLifetime` | Exit / restart application |
| `IClipboardService` | `WpfClipboardService` | Clipboard read/write |
| `IGitHubReleaseService` | `GitHubReleaseService` | Fetch GitHub releases list and assets |
| `IImageCacheService` | `ImageCacheService` | On-disk image cache |
| `ILocalizationService` | `LocalizationService` | Switch language at runtime (reload XAML resource dictionaries) |
| `ILocalizedTextProvider` | `WpfLocalizedTextProvider` | Read localized strings from code |
| `ISearchHistoryService` | `SearchHistoryService` | Persist search history |
| `ISettingsService` | `SettingsService` | App configuration read/write |
| `ITeamOverlayService` | `TeamOverlayService` | Bottom-right team overlay during hero selection |
| `ITipMessageService` | `TipMessageService` | Global toast / tip messages |
| `IUIDispatcher` | `WpfUIDispatcher` | Cross-thread UI dispatch wrapper |
| `IUpdateService` | `UpdateService` | Compare versions, resolve latest release zip URL |

`GameMonitor`, `Ocr`, `ScreenCapture` each expose their own interfaces (`IGameLogMonitor` / `IGameStatusMonitor` / `IPlayerPrefsService`, `IOcrService`, `IScreenCaptureService`), registered into the DI container via their `*AutoRegister.cs`.

---

## Core Module Details

### 1. MVVM Architecture (Prism + DryIoc)

- All ViewModels inherit from `ViewModelBase` with `RaisePropertyChanged()` (no `SetProperty` wrapper per [CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md) conventions)
- Property change notifications use `nameof()` or `[CallerMemberName]`, string literals forbidden
- ViewModels **must not reference WPF types** (`Visibility`, `Brush`, `Color`, etc.); express visibility as `bool` + Converter
- Navigation via `IMainContentNavigationService` with forward/back support
- Cross-module communication via `IEventAggregator` (e.g. `TipMessageEvent`, `SettingsChangedEvent`)

### 2. On-Demand Module Loading

Each of the 9 UI pages is a Prism `IModule` registered as `OnDemand` in `ModuleCatalogConfigManager`. Modules are only loaded on first navigation, reducing startup time.

```csharp
// PageNames.cs
public static class PageNames
{
    public const string HomePage               = nameof(HomePage);
    public const string StatsPage              = nameof(StatsPage);
    public const string SearchPage             = nameof(SearchPage);
    public const string TeamInfoPage           = nameof(TeamInfoPage);
    public const string SettingsPage           = nameof(SettingsPage);
    public const string AnnouncementPage       = nameof(AnnouncementPage);
    public const string ClosePromptPage        = nameof(ClosePromptPage);
    public const string FeedbackPage           = nameof(FeedbackPage);
    public const string UpdateNotificationPage = nameof(UpdateNotificationPage);
}
```

### 3. Game Status Monitoring (GameMonitor)

`GameLogMonitor` watches `Player.log` using **`FileSystemWatcher` plus a polling fallback**. Internally the responsibilities are split into:

- `LogReader` (file IO)
- `LogPoller` (poll loop)
- `BattleStateMachine` (battle state)

The outer `GameLogMonitor` is just a façade that orchestrates lifetime and event dispatch. Detected events:

- `BattleJoined` — hero selection (parses RoomId from log)
- `BattleStarted` — match start (parses BattleId)
- `BattleEnded` — match end

`GameStatusMonitor` maintains a state machine, notifying pages of the current phase (`HeroSelection` / `InGame` / `BattleEnded`). `HomePageViewModel` additionally uses `Process.GetProcessesByName("NarakaBladepoint")` as a secondary check.

### 4. Screen Capture & OCR (Team Info)

1. `GameStatusMonitor` detects `HeroSelection` state
2. `TeamInfoPageViewModel` starts the OCR polling loop
3. `ScreenCaptureService` captures the game window via **Windows Graphics Capture API** (native C++/WinRT DLL → SharpDX D3D11), reusing full-frame buffers from `ArrayPool` and slicing three regions via `ScreenQuadrant` into a single composite image
4. `OcrEngine` talks to **PaddleOCR-json.exe** as a **singleton long-lived child process** over stdin/stdout pipes using `image_base64` (zero disk IO). Models load once on the first `PrewarmAsync` call (~600–1500 ms); subsequent calls only run inference (~100–250 ms). A `JobObject` guarantees the child is reaped by the OS if the host crashes
5. `TeamInfoOcrService` / `TeamOcrCoordinator` parse the OCR output and extract teammate nicknames
6. The stats API is queried for each teammate and the results are displayed side-by-side

### 5. Source-Generated HTTP Client

API clients are **not hand-written**. `BlackGoldAncientSword.Framework.SourceGenerator` reads JSON definitions from `Http/Definitions/*.json` at compile time and generates strongly-typed code:

- JSON files describe endpoints, request/response data structures, and enums
- The generator is a **Roslyn Source Generator**, referenced by both Framework and Tests as an Analyzer
- The `BgaSourceGenMode` MSBuild property switches output:
  - `Client` mode (Framework) — generates `NarakaApiClient` + DTOs
  - `Tests` mode (Tests) — generates HTTP API test code

### 6. Localization

Multi-language support via WPF `ResourceDictionary`. All UI text is defined in `Strings.{zh-CN,en,zh-TW}.xaml`. `ILocalizationService.ApplyLanguage()` dynamically swaps resource dictionaries at runtime — no restart needed.

### 7. Online Update (Standalone Updater Process)

Updates are a two-process collaboration:

- **Main app side** (`Modules/UI/UpdateNotification/ViewModels/UpdateNotificationPageViewModel.cs`)
  - Detects new versions via `IUpdateService` + `IGitHubReleaseService`
  - On "Update Online" click, launches `BlackGoldAncientSword.Update.exe` in the install directory with `--url <zip URL>`, `--target <install dir>`, `--main-exe BlackGoldAncientSword.App.exe`
- **Updater side** (`BlackGoldAncientSword.Update`)
  - Standalone process. Does not reference any business project (HandyControl only) — avoids DLL locking so the whole install directory can be safely overlaid
  - `UpdaterRunner` orchestrates: download zip (0–90%) → extract (90–98%) → prompt to close main app → full overlay → relaunch main app → exit
  - Published as self-contained + `PublishSingleFile` + `EnableCompressionInSingleFile`

---

## Build & Run

### Prerequisites

- Windows 10/11 x64
- .NET 10.0 SDK
- PowerShell 7+ recommended (UTF-8 environment)

### Build

```powershell
# Restore + build the whole solution
dotnet build src/BlackGoldAncientSword.slnx

# Build only the main app
dotnet build src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Debug

# Release publish main app (self-contained single-file .exe)
dotnet publish src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Release -o publish/App

# Release publish updater (self-contained single-file .exe, must sit next to the main app)
dotnet publish src/BlackGoldAncientSword.Update/BlackGoldAncientSword.Update.csproj -c Release -o publish/Updater
```

> Project rule: after any code change, run `dotnet build src/BlackGoldAncientSword.slnx` and reach 0 errors before considering the change done.

### Run Tests

```powershell
dotnet test src/BlackGoldAncientSword.Tests/BlackGoldAncientSword.Tests.csproj
```

---

## CI / Release Flow

Two workflows under `.github/workflows/`:

| Workflow | Trigger | Purpose |
|---|---|---|
| `main-build.yml` | push / PR → `main` | Validation build (`dotnet build src/BlackGoldAncientSword.slnx`), no publish |
| `dotnet-desktop.yml` | push → `release` | Full release: bump version → build App → publish App + Updater (self-contained) → pack zip + Inno Setup installer → create GitHub Release |

Release flow highlights:

1. Infer version from existing git tags (`v*.*.*.*` pattern), auto-increment the build segment
2. Patch `App.csproj`: `Version` / `AssemblyVersion` / `FileVersion`
3. Publish both App and Updater as self-contained single-file .exe
4. Merge publish outputs → zip as `BlackGoldAncientSword-v{version}.zip`
5. Build `BlackGoldAncientSword-{version}-win-x64-Setup.exe` via `setup.iss` (Inno Setup script)
6. Create a GitHub Release with auto-generated commit-title list since the previous tag

> The `release` branch has branch protection enabled: no direct push, no force push, no deletion, and `enforce_admins` is on (so administrators are bound by the same rules). Every change must land via a pull request. Day-to-day work happens on `main`; to ship a release, first commit + push `main` via the [git-commit](.claude/skills/git-commit/SKILL.md) skill, then open a `main` → `release` pull request on GitHub and merge it — that merge is what triggers `dotnet-desktop.yml`.

---

### Special Thanks

- WeChat: craftwyrd

---

## License

MIT License. Author: **小窗同学** (XiaoChuang).
