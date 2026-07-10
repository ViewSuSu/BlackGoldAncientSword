[中文](README.md) | [English](README.en.md)

[![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?style=flat&logo=windows&logoColor=white)]() [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)]() [![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20Prism%208.1-purple?style=flat)]() [![License](https://img.shields.io/badge/License-MIT-green?style=flat)](LICENSE)

- **GitHub repository**: https://github.com/ViewSuSu/BlackGoldAncientSword
- **Gitee mirror**: https://gitee.com/SususuChang/BlackGoldAncientSword

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

## Sign-in 🔐

On startup, the app guides you through a one-time sign-in (slider CAPTCHA → WeChat QR scan). The resulting token is encrypted with **Windows DPAPI** and stored locally, then silently refreshed — no repeated scans needed. When any background request returns 401, a **concurrent single-flight** overlay pops up: a single scan resumes every pending request. All API calls go through the **in-house P7 signature protocol** with a Bearer token, targeting `desktop.naraka.drivod.top`.

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
- **Account avatar**: Click the top-right avatar to open a popup showing nickname / membership info, with a one-click sign-out button
- **Check for updates**: Manually check and download new releases (delegates to the standalone Updater process, see below)
- **Current version**

<p align="center">
  <img src="docs/images/04_settings.png" alt="Settings screenshot" /><br />
  <small><u>Settings</u></small>
</p>

---

## Online Update

At launch the app raises a **StartupGate overlay**: until the update check finishes, the entire UI (sign-in button / sidebar / close prompt) is blocked so user actions cannot race the update flow. If a new version is detected, the notification page pops up and **locks the rest of startup (UpdateGate)** — the user must respond first (Update Online / Open Browser / Later / Close) before the sign-in gate and the rest of navigation resume. Clicking "Update Online" then:

1. Launches the standalone **BlackGoldAncientSword.Update.exe** (the updater) with the download URL, install directory, and main-app file name as arguments;
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

**Q: Why is the installer / program so large?**

The app is published as a **self-contained** deployment, which bundles the .NET runtime so users can run it without installing .NET separately. The built-in OCR engine also relies on the **PP-OCRv5 ONNX models (~22MB) + ONNX Runtime native libraries + SkiaSharp image codecs**, totalling roughly 50MB. These native AI/vision components, together with the .NET runtime, account for the bulk of the download size. Without them, automatic OCR of teammate nicknames would not be possible — the size is a necessary price 😅.

> Starting with v1.0.0.3, the OCR engine has switched from the PaddleOCR-json subprocess design (which dragged in ~150MB of Paddle Inference + MKL + OpenCV native DLLs) to RapidOcrNet running in-process on ONNX Runtime, with the recognizer upgraded to PP-OCRv5. The installer is about 100MB lighter, and character coverage for things like Japanese kana and Latin-Extended is substantially better.

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

`src/BlackGoldAncientSword.slnx` contains **11 projects**: 7 class libraries + 3 executables (main App, standalone Updater, offline Downloader) + 1 Roslyn analyzer + 1 test project.

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
┌──────────────────────────┐  ← shipped standalone, not a runtime dep of App
│ BlackGoldAncientSword.   │
│ Downloader (offline dl,  │  Streams split installer from Gitee →
│ WinExe)                  │  launches Setup.exe → self-exits
└──────────────────────────┘
                                                      │
        ┌─────────────────────┬──────────────────────┘
        │                     │
        ▼                     ▼                     ▼
┌────────────┐        ┌──────────────┐        ┌───────────┐
│  Modules   │        │  Framework   │        │ Resources │
│ (10 UI page│ ◄────► │ (Core + 13   │ ◄──────│ (i18n XAML│
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
│     Ocr      │ │ RapidOcrNet +    │
│ (in-process) │ │ PP-OCRv5 (ONNX)  │
└──────────────┘ └──────────────────┘
```

### Layer Map

| Layer | Project | Output | Responsibility |
|---|---|---|---|
| **Main App** | `BlackGoldAncientSword.App` | WinExe | App entry, main window, sidebar nav, tray, launches Updater, background OCR prewarm at startup, concrete implementations of the three sign-in / startup / update gates (AuthChallenge / StartupGate / UpdateGate) |
| **Updater** | `BlackGoldAncientSword.Update` | WinExe | Standalone online-update process, zero business deps (HandyControl only) |
| **Offline Downloader** | `BlackGoldAncientSword.Downloader` | WinExe | Standalone single-file exe. Streams split installer from Gitee release → launches Setup.exe → self-exits. Zero API deps (uses 302 + CDN) |
| **UI Modules** | `BlackGoldAncientSword.Modules` | ClassLib | 11 Prism `IModule` pages (including the sign-in overlay), on-demand loading |
| **Core Framework** | `BlackGoldAncientSword.Framework` | ClassLib | MVVM base, Prism infra, service abstractions/implementations, HTTP API (with P7 signature / Auth Token / slider CAPTCHA / WeChat QR / unified DTO mapping) |
| **Game Monitor** | `BlackGoldAncientSword.GameMonitor` | ClassLib | Process detection, Player.log parsing, battle state machine |
| **Screen Capture** | `BlackGoldAncientSword.ScreenCapture` | ClassLib | Windows Graphics Capture API + SharpDX + native `wgc_capture.dll` |
| **OCR Engine** | `BlackGoldAncientSword.Ocr` | ClassLib | RapidOcrNet (PP-OCRv5 ONNX) in-process inference wrapper |
| **Resources** | `BlackGoldAncientSword.Resources` | ClassLib | Multi-language XAML resource dictionaries, icons, images |
| **Source Gen** | `BlackGoldAncientSword.Framework.SourceGenerator` | Roslyn Analyzer | Compile-time HTTP client + test code generation from JSON |
| **Tests** | `BlackGoldAncientSword.Tests` | xUnit | OCR, screen capture, game monitor, HTTP, update tests |

---

## Tech Stack

| Category | Technology / Library | Purpose |
|---|---|---|
| **Runtime** | .NET 10.0 (`net10.0-windows`) | Target framework |
| **UI** | WPF + HandyControl 3.5 | Desktop UI and control library |
| **Theme** | Custom ModernTheme (celadon / bamboo-green eye-friendly palette) | Soft pale-green surface + deep-green accent + ink text, comfortable for long reading sessions |
| **MVVM** | Prism 8.1 (`Prism.DryIoc`) | DI container, region navigation, modularization |
| **HTTP** | Compile-time source generator + `HttpClient` + `DelegatingHandler` | Generate strongly-typed API clients from `api-definitions.json`; request pipeline chains `SignatureHandler` (P7 protocol signing) and `AuthTokenHandler` (Bearer + 401 single-flight refresh) |
| **Auth** | In-house slider CAPTCHA + WeChat QR sign-in + JWT + Windows DPAPI | Sign-in token encrypted at rest, refreshed before expiry, 401 triggers a single-flight sign-in overlay |
| **Mapping** | Mapster 7.4 | DTO ↔ ViewModel |
| **JSON** | `System.Text.Json` (with source-generated context) | Serialization / deserialization (fully replaced Newtonsoft.Json) |
| **Screen Capture** | SharpDX.Direct3D11 + native WGC DLL (C++/WinRT) | Game window capture |
| **OCR** | RapidOcrNet 2.0.0 + ONNX Runtime 1.24 (in-process, PP-OCRv5 models) | Multi-language text recognition (single model covers ZH / EN / JA / Latin / Cyrillic, etc.) |
| **Image I/O** | SkiaSharp 3.119 | OCR entry-point `byte[]` → `SKBitmap` decode |
| **System Tray** | Hardcodet.NotifyIcon.Wpf | Tray icon and context menu |
| **Tests** | xUnit + Moq | Unit and integration tests |
| **Packaging** | Self-Contained + PublishSingleFile | Both App and Updater shipped as single-file .exe (win-x64) |
| **Installer** | Inno Setup | Produces `BlackGoldAncientSword-{version}-win-x64-Setup.exe` |

---

## Project Structure

```
src/
├── BlackGoldAncientSword.App/              # WPF main entry (WinExe)
│   ├── App.xaml / App.xaml.cs              # App entry, Prism bootstrap, startup flow orchestration (StartupGate → update check → UpdateGate → AuthChallenge → navigate to Home)
│   ├── AppAssemblyMarker.cs                # Assembly locator (for XAML resource resolution)
│   ├── Services/                           # App-layer implementations of the three gates (depend on IRegionManager / UI Dispatcher, cannot live in Framework)
│   │   ├── StartupGateService.cs           # Startup overlay latch (one-way true → false)
│   │   ├── UpdateGateService.cs            # Update prompt gate (TCS single-flight + completed latch to cover the race where Complete arrives before WaitAsync)
│   │   └── AuthChallengeService.cs         # 401 single-flight: any number of concurrent 401s only pop the sign-in overlay once
│   └── Shell/
│       ├── MainWindow.xaml(.cs)            # Shell (sidebar + nav + tray + avatar popup + startup overlay layer)
│       └── MainWindowViewModel.cs          # Nav commands, game status, update detection, user info
│
├── BlackGoldAncientSword.Update/           # Standalone online updater (WinExe, zero business deps)
│   ├── App.xaml(.cs)                       # Entry: parses --url / --target / --main-exe
│   ├── Services/
│   │   ├── UpdateOptions.cs                # Command-line argument model
│   │   └── UpdaterRunner.cs                # Orchestrates: download → extract → close main → overlay → relaunch
│   ├── Shell/UpdateWindow.xaml(.cs)        # Progress window
│   └── ViewModels/UpdateViewModel.cs       # Progress & status bindings
│
├── BlackGoldAncientSword.Downloader/       # Offline installer downloader (WinExe, standalone single-file)
│   ├── App.xaml(.cs)                       # Entry; process-level temp cleanup safety net
│   ├── Services/
│   │   ├── DownloaderRunner.cs             # Orchestrates: query Gitee latest → stream split volumes → launch Setup
│   │   ├── GiteeAssetsFetcher.cs           # Uses 302 + CDN — no Gitee REST API calls (avoids rate limits)
│   │   └── InstallerForegrounder.cs        # Launches Setup.exe and brings it to foreground
│   ├── Shell/DownloadWindow.xaml(.cs)      # Progress window (bar + 4-stat live refresh)
│   └── ViewModels/DownloadViewModel.cs
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
│   │   ├── Auth/                           # Authentication subsystem (in-house P7 signature + slider CAPTCHA + WeChat QR + JWT + DPAPI)
│   │   │   ├── ApiSignature/               # P7 request signing: SignatureHandler / RequestSigner / ISignatureTicketProvider
│   │   │   ├── Captcha/                    # AJ slider CAPTCHA: AjCaptchaService + AesEcbCipher
│   │   │   ├── WechatQr/                   # WeChat QR sign-in polling: WechatQrLoginService
│   │   │   ├── Token/                      # Bearer token lifecycle: AuthTokenHandler (DelegatingHandler + 401 single-flight refresh) + AuthTokenState + AuthTokenRefresher + JwtExpiryReader + AuthTokenExpiryMonitor + DpapiAuthTokenStore (Windows DPAPI CurrentUser encrypted at rest)
│   │   │   ├── MemberProfile/              # Membership info lookup (used by the avatar popup)
│   │   │   └── SignedOnlyHttpClient.cs     # HttpClient that only mounts the signature handler, not Bearer — for sign-in-time APIs (avoids AuthTokenHandler's recursive 401 interception)
│   │   ├── Unified/                        # DTO normalization layer: maps different APIs' PlayerStats / Season / RecentBattle / BattleDetail into UnifiedXxx so the UI layer consumes them uniformly
│   │   └── JsonFlexibleStringConverter.cs  # Fault-tolerant System.Text.Json converter
│   ├── Services/
│   │   ├── Abstractions/                   # 17 service interfaces (see table below)
│   │   └── Implementation/                 # Service implementations (some interfaces are implemented in the App layer)
│   ├── Themes/Generic.xaml                 # HandyControl theme
│   └── UI/Controls/                        # Custom WPF controls (DataGridWrapPanel, etc.)
│
├── BlackGoldAncientSword.Framework.SourceGenerator/  # Roslyn source generator
│   ├── ApiDefinitionsParser.cs             # Parses api-definitions.json
│   ├── EnumSourceGenerator.cs              # Generates enum types
│   ├── HttpApiSourceGenerator.cs           # Generates NarakaApiClient + DTOs (Client mode)
│   └── HttpApiTestSourceGenerator.cs       # Generates HTTP API test code (Tests mode)
│
├── BlackGoldAncientSword.Modules/          # UI page modules (11 Prism IModule)
│   ├── Mappings/BattleMappingRegister.cs   # Mapster mapping registration
│   ├── Module/                             # 11 IModule registrations
│   │   ├── AnnouncementModule.cs           # Announcements
│   │   ├── AuthChallengeModule.cs          # Sign-in overlay (slider → WeChat QR state machine)
│   │   ├── BattleDetailModule.cs           # Battle detail overlay (personal / team / top5 tabs)
│   │   ├── ClosePromptModule.cs            # Close confirmation dialog
│   │   ├── FeedbackModule.cs               # Feedback
│   │   ├── HomeModule.cs                   # Home (game status monitor)
│   │   ├── SearchModule.cs                 # Search history
│   │   ├── SettingsModule.cs               # Settings
│   │   ├── StatsModule.cs                  # Player stats (with 350ms search debounce)
│   │   ├── TeamInfoModule.cs               # Team info (OCR + comparison)
│   │   └── UpdateNotificationModule.cs     # New version prompt / launch Updater / release notes fetch
│   └── UI/                                 # ViewModels + Views per module
│       ├── AuthChallenge/                  # Sign-in page: Loading→CaptchaPending→CaptchaVerifying→QrLoading→QrPolling→Success/Failed
│       ├── BattleDetail/                   # Battle detail: parallel fetch personal / team / top5
│       ├── Stats/Services/                 # Stats aggregation services (consume UnifiedXxx DTOs)
│       ├── TeamInfo/Services/              # TeamInfoOcrService, TeamOcrCoordinator
│       └── UpdateNotification/ViewModels/  # Launches BlackGoldAncientSword.Update.exe / shows release notes via IReleaseNotesFetcher
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
├── BlackGoldAncientSword.Ocr/              # OCR engine (in-process ONNX Runtime)
│   ├── IOcrService.cs                      # Service interface
│   ├── OcrEngine.cs                        # RapidOcrNet (PP-OCRv5) wrapper
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

ocr_engine/                                 # PP-OCRv5 ONNX models + dict (copied to output by Ocr)
└── models/v5/                              # PP-OCRv5 Chinese mobile models (~22MB total)
    ├── ch_PP-OCRv5_det_mobile.onnx         # Text detection (DBNet)
    ├── ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx  # Orientation classifier
    ├── ch_PP-OCRv5_rec_mobile.onnx         # Character recognition (CRNN)
    └── ppocrv5_dict.txt                    # Dictionary (18,383 characters; covers ZH/EN/JA/Latin-Extended)
```

---

## Framework Service Interfaces

`BlackGoldAncientSword.Framework/Services/Abstractions/` exposes 17 public interfaces:

| Interface | Main Implementation | Location | Purpose |
|---|---|---|---|
| `IAppAssemblyMarker` | `AppAssemblyMarker` | App | Assembly locator marker (for XAML resource resolution) |
| `IApplicationLifetime` | `WpfApplicationLifetime` | Framework | Exit / restart application |
| `IAuthChallengeService` | `AuthChallengeService` | App | Concurrent single-flight sign-in overlay on 401; all awaiters resume together (depends on `IRegionManager` / `IModuleManager` / `IUpdateGateService`) |
| `IClipboardService` | `WpfClipboardService` | Framework | Clipboard read/write |
| `IGiteeReleaseService` | `GiteeReleaseService` | Framework | Fetch Gitee releases list and assets (uses 302 tag probe + CDN HEAD probing, zero API deps) |
| `IImageCacheService` | `ImageCacheService` | Framework | On-disk image cache |
| `ILocalizationService` | `LocalizationService` | Framework | Switch language at runtime (reload XAML resource dictionaries) |
| `ILocalizedTextProvider` | `WpfLocalizedTextProvider` | Framework | Read localized strings from code |
| `IReleaseNotesFetcher` | `GiteeReleaseNotesFetcher` | Framework | Fetch a Gitee release description (tag body) via the web-page 302 rather than `/api/v5`, to avoid unauthenticated IPs hitting the 60 req/min rate limit |
| `ISearchHistoryService` | `SearchHistoryService` | Framework | Persist search history |
| `ISettingsService` | `SettingsService` | Framework | App configuration read/write |
| `IStartupGateService` | `StartupGateService` | App | Startup latch that blocks all UI interaction until the update check returns (mask the whole UI between Shell display and `CheckForUpdatesAsync` completion; `Complete` may only fire once) |
| `ITeamOverlayService` | `TeamOverlayService` | Framework | Bottom-right team overlay during hero selection |
| `ITipMessageService` | `TipMessageService` | Framework | Global toast / tip messages |
| `IUIDispatcher` | `WpfUIDispatcher` | Framework | Cross-thread UI dispatch wrapper |
| `IUpdateGateService` | `UpdateGateService` | App | Startup "new version" gate: `AuthChallengeService.ShowAsync` awaits `WaitAsync` first; flow only resumes once the user makes a choice in the update prompt and `Complete` is called |
| `IUpdateService` | `UpdateService` | Framework | Compare versions, resolve latest Gitee release Setup / zip / split-volume URLs (via 302 + CDN, avoids API rate limits) |

The three `*Gate*` / `AuthChallenge` interfaces are implemented under `App/Services/` (not `Framework/Services/Implementation/`) because they need `IRegionManager` / UI Dispatcher — runtime dependencies that only live in the main app.

`GameMonitor`, `Ocr`, `ScreenCapture` each expose their own interfaces (`IGameLogMonitor` / `IGameStatusMonitor` / `IPlayerPrefsService`, `IOcrService`, `IScreenCaptureService`), registered into the DI container via their `*AutoRegister.cs`.

`Framework/Http/Auth/` and `Framework/Http/Unified/` also expose a family of authentication and DTO interfaces such as `ISignatureTicketProvider`, `ISignedOnlyHttpClient`, `IAjCaptchaService`, `IWechatQrLoginService`, `IAuthTokenStore`, `IAuthTokenState`, `IAuthTokenRefresher`, `IMemberProfileService`, all wired into the DI container via `[Component]` attributes.

---

## Core Module Details

### 1. MVVM Architecture (Prism + DryIoc)

- All ViewModels inherit from `ViewModelBase` with `RaisePropertyChanged()` (no `SetProperty` wrapper per [CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md) conventions)
- Property change notifications use `nameof()` or `[CallerMemberName]`, string literals forbidden
- ViewModels **must not reference WPF types** (`Visibility`, `Brush`, `Color`, etc.); express visibility as `bool` + Converter
- Navigation via `IMainContentNavigationService` with forward/back support
- Cross-module communication via `IEventAggregator` (e.g. `TipMessageEvent`, `SettingsChangedEvent`)

### 2. On-Demand Module Loading

Each of the 11 UI pages is a Prism `IModule` registered as `OnDemand` in `ModuleCatalogConfigManager`. Modules are only loaded on first navigation, reducing startup time.

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
    public const string BattleDetailPage       = nameof(BattleDetailPage);
    public const string AuthChallengePage      = nameof(AuthChallengePage);
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
4. `OcrEngine` runs **RapidOcrNet + ONNX Runtime in-process**: `byte[]` → SkiaSharp decodes to `SKBitmap` → a single `Detect()` call chains three ONNX inferences (det / DBNet → cls / orientation, optional → rec / CRNN). Models + dictionary load on the first `PrewarmAsync` call (~200–500 ms) and the same call runs a tiny inference to trigger ONNX session buffer / kernel JIT. A `SemaphoreSlim` serializes access to RapidOcr's internal state. No subprocess, no IPC, no JobObject
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
  - Detects new versions via `IUpdateService` (Gitee release page 302 tag probe + CDN HEAD probing for split volumes)
  - Fetches the release body via `IReleaseNotesFetcher` from the tag page (also 302-based, avoiding `/api/v5` rate limits) and renders it in the prompt
  - On "Update Online" click, launches `BlackGoldAncientSword.Update.exe` in the install directory with `--url <zip URL>`, `--target <install dir>`, `--main-exe BlackGoldAncientSword.App.exe`
  - For users with no network or GitHub blocked, `BlackGoldAncientSword-win-x64-Downloader.exe` on the Gitee release page streams the split installer and launches Setup automatically
- **Updater side** (`BlackGoldAncientSword.Update`)
  - Standalone process. Does not reference any business project (HandyControl only) — avoids DLL locking so the whole install directory can be safely overlaid
  - `UpdaterRunner` orchestrates: download zip (0–90%) → extract (90–98%) → prompt to close main app → full overlay → relaunch main app → exit
  - Published as self-contained + `PublishSingleFile` + `EnableCompressionInSingleFile`

### 8. Authentication & Startup Flow (Three Gates)

The startup flow is serialized through three latches/gates to avoid races:

```text
Shell shown
   │
   ▼
StartupGate (IsBusy=true, full-UI overlay blocks all interaction)
   │
   ▼
CheckForUpdatesAsync (fetch Gitee latest release + release notes)
   │
   ▼
StartupGate.Complete()   ← must fire exactly once — success, failure, or exception — otherwise the overlay never lifts
   │
   ▼
if a new version exists → navigate to UpdateNotificationPage prompt
                          user acts → UpdateGate.Complete()
                          │
                          ▼
AuthChallengeService.ShowAsync (await UpdateGate.WaitAsync first)
   │
   ▼
if no valid local token → show AuthChallengePage
     ├── Loading → fetch slider CAPTCHA
     ├── CaptchaPending / CaptchaVerifying → AjCaptchaService.SolveAsync (AES-ECB-encrypted trajectory)
     ├── QrLoading → fetch WeChat QR code
     ├── QrPolling → poll scan status every 2s
     └── Success → AuthTokenStore persists (DPAPI CurrentUser encrypted)
   │
   ▼
Navigate to HomePage / StatsPage / …
```

**Runtime 401 single-flight**: when any API returns 401, `AuthTokenHandler` first tries `AuthTokenRefresher.RefreshAsync` (exchange refresh_token for a new access_token); on failure it invokes `AuthChallengeService.ShowAsync`. **Any number of concurrent 401s only pop the overlay once** — after the user signs in, every awaiter resumes and the original requests are replayed.

**Token storage**: `DpapiAuthTokenStore` calls `ProtectedData.Protect(scope=CurrentUser)` and writes the ciphertext under the user profile directory; only the same Windows account can decrypt it. `AuthTokenExpiryMonitor` reads `exp` via `JwtExpiryReader` and proactively refreshes 60s before expiry, so real requests don't have to hit 401 first.

### 9. HTTP Request Pipeline (P7 Signing + 401 Single-Flight)

Aside from the sign-in-only `SignedOnlyHttpClient` (signature but no Bearer), the business `HttpClient` chains:

```text
Business request
   │
   ▼
SignatureHandler        ← pulls a ticket from ISignatureTicketProvider, signs URL/body/timestamp per the P7 protocol, writes custom headers
   │
   ▼
AuthTokenHandler        ← attaches the Bearer token; on 401 refreshes first, and if refresh fails calls AuthChallengeService.ShowAsync and replays the request after the user signs in
   │
   ▼
HttpClientHandler       ← actually sends to https://desktop.naraka.drivod.top
```

The API base address has migrated from `naraka.drivod.top` to `desktop.naraka.drivod.top` (the dedicated P7 desktop-client domain).

### 10. Stats Search Debounce

`StatsPageViewModel` debounces the search box by 350ms — while the user is typing, only the last input is honored, cutting wasted API calls (especially valuable when the user isn't signed in yet and every request would otherwise trigger a CAPTCHA + QR round-trip).

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

# Release publish offline downloader (self-contained single-file .exe, shipped as a standalone Release asset, not bundled into the install directory)
dotnet publish src/BlackGoldAncientSword.Downloader/BlackGoldAncientSword.Downloader.csproj -c Release -o publish/Downloader
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
| `dotnet-desktop.yml` | push → `release` | Full release: bump version → build App → publish App + Updater + Downloader (self-contained) → pack zip + Inno Setup full / split installers → create GitHub Release |

Release flow highlights:

1. Infer version from existing git tags (`v*.*.*.*` pattern), auto-increment the build segment
2. Patch `App.csproj`: `Version` / `AssemblyVersion` / `FileVersion`
3. Publish App, Updater, and Downloader as self-contained single-file .exe
4. Merge build + publish outputs, drop the Updater exe into the install directory, then zip as `BlackGoldAncientSword-v{version}.zip` + 7z split volumes `-split.zip.NNN` (≤99MB each)
5. Build both a full installer `BlackGoldAncientSword-{version}-win-x64-Setup.exe` and a DiskSpanning split installer `-Split.exe` + `.bin` via `setup.iss`
6. Produce versionless aliases (`BlackGoldAncientSword-win-x64-Setup.exe` / `-Downloader.exe`) that pair with `/releases/latest/download/` magic redirects for permanent "latest version" share links
7. Create a GitHub Release with auto-generated commit-title list since the previous tag

> The `release` branch has branch protection enabled: no direct push, no force push, no deletion, and `enforce_admins` is on (so administrators are bound by the same rules). Every change must land via a pull request. Day-to-day work happens on `main`; to ship a release, first commit + push `main` via the [git-commit](.claude/skills/git-commit/SKILL.md) skill, then open a `main` → `release` pull request on GitHub and merge it — that merge is what triggers `dotnet-desktop.yml`.

---

### Special Thanks

- WeChat: craftwyrd

---

## License

MIT License. Author: **小窗同学** (XiaoChuang).
