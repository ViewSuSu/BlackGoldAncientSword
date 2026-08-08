[中文](README.md) | [English](README.en.md)

[![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D6?style=flat&logo=windows&logoColor=white)]() [![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)]() [![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20Prism%208.1-purple?style=flat)]() [![License](https://img.shields.io/badge/License-MIT-green?style=flat)](LICENSE)

- **GitHub 仓库**：https://github.com/ViewSuSu/BlackGoldAncientSword
- **Gitee 镜像**：https://gitee.com/SususuChang/BlackGoldAncientSword

# 黑金古刀-永劫助手（BlackGoldAncientSword）

> 查询《永劫无间》（NARAKA: BLADEPOINT）玩家战绩数据的桌面辅助工具。

> 该项目受到 [Zzaphkiel/Seraphine](https://github.com/Zzaphkiel/Seraphine) 的鼓舞，感谢先驱者们做出的贡献。

---

## 下载 📥

[![Download](https://img.shields.io/badge/下载-最新版本-blue?style=flat&logo=github)](https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest)

点击上方按钮即可直接下载最新版本的 .exe 安装包。

# 用户手册

## 简介

**黑金古刀-永劫助手**是一款运行在 Windows 上的桌面应用。它可以在游戏过程中自动检测游戏状态、识别队友信息，并实时查询玩家战绩数据。无需切出游戏打开网页，助手将战绩数据直接呈现在桌面端，支持**三排 / 双排 / 单排**及**排位 / 匹配 / 天人**模式的完整数据统计。

## 登录 🔐

助手启动时会引导用户完成一次登录（滑块验证 → 微信扫码），登录 token 通过 **Windows DPAPI** 加密保存到本地，之后自动续期，无需重复扫码。任意后台请求返回 401 时会**并发单飞**地弹出登录 Overlay，一次扫码所有等待中的请求同时恢复。所有 API 请求走**自研 P7 签名协议**并带 Bearer token 访问 `desktop.naraka.drivod.top`。

## 战绩查询

战绩页面是助手的核心功能。在搜索框中输入玩家昵称，即可查询该玩家的完整战绩数据：

- **赛季数据总览**：K/D、第一率、前五率、场均击败、场均治疗、场均助攻、场均生存
- **最高记录**：最高击败、最高治疗、最高助攻、最高伤害、最多振刀
- **段位信息**：当前赛季段位分数与段位名称（天选模式含星数）
- **最近 10 场对局**：每局英雄、模式、击败/伤害、段位分变化（含 ± 差值）、荣誉称号

支持切换赛季、模式类别（排位 / 匹配 / 天人）和队伍规模（三排 / 双排 / 单排）。

<p align="center">
  <img src="docs/images/02_stats.png" alt="战绩截图" /><br />
  <small><u>战绩</u></small>
</p>

> 点击玩家昵称旁的复制按钮可快速复制昵称或 UID。

---

## 队伍信息 —— 智能识别

进入游戏英雄选择界面后，助手会自动从 CCMini 语音日志中解析队友 UID，将队友的赛季战绩数据并排展示，方便快速评估队伍实力。

- 自动识别队友（无需手动输入）
- 支持三排 / 双排队伍
- 支持排位 / 匹配 / 天人模式与赛季切换
- 队伍成员关键数据对比展示（含与本地玩家的差值 diff）
- 进入对局后持续监听队友退出 / 换人，实时更新卡片

<p align="center">
  <img src="docs/images/03_team_info.png" alt="队伍信息截图" /><br />
  <small><u>队伍信息识别 1</u></small>
</p>

<p align="center">
  <img src="docs/images/03_team_info_recognition.png" alt="队伍信息识别效果" /><br />
  <small><u>队伍信息识别 2</u></small>
</p>

> 可随时点击"重新识别队伍信息"按钮手动触发识别。当部分名字识别错误时，可直接修改识别出的名字并重新查询。

---

## 设置

设置页面集中管理应用配置：

- **数据保存路径**：战绩数据的本地存储目录（支持自定义 + 旧数据自动迁移）
- **缓存路径**：图片缓存目录（含缓存大小显示与一键清理）
- **日志路径**：日志输出目录（含日志大小显示与一键清理）
- **全局字号**：滑块调节界面字号（含默认值与放大档位说明）
- **语言**：支持 简体中文 / English / 繁體中文
- **关闭行为**：点击关闭按钮时的默认行为，可选"最小化到任务栏 / 直接退出"，并支持记住选项
- **英雄选择时的右下角队伍提示弹窗**：开关控制
- **账号头像**：顶部头像点击弹出 Popup，展示昵称 / 会员信息，支持一键退出登录
- **检查更新**：手动检查与下载新版本（调用独立的 Update 程序在线更新，详见下文）
- **当前版本**：显示版本号

<p align="center">
  <img src="docs/images/04_settings.png" alt="设置截图" /><br />
  <small><u>设置</u></small>
</p>

---

## 在线更新

助手启动时会先打上**启动遮罩（StartupGate）**：在检测更新完成前，整个 UI（登录按钮 / 侧边栏 / 关闭确认）都被拦截，避免用户操作与更新流程打架。检测到新版本时会弹出更新提示页面并**锁死后续流程（UpdateGate）**——用户必须先处理弹窗（在线更新 / 打开浏览器 / 稍后再说 / 关闭），登录 gate 与后续导航才会继续。点击"在线更新"即可：

1. 主程序拉起独立的 **BlackGoldAncientSword.Update.exe**（更新器）并传入下载地址、安装目录、主程序文件名等参数；
2. 更新器下载新版 zip → 解压 → 全量覆盖安装目录 → 重新拉起主程序 → 自身退出。

> 更新器与主程序完全解耦（不引用 App / Framework / Modules），因此覆盖文件时不会被 DLL 锁定。

---

## 其他功能

### 系统托盘

助手支持最小化到系统托盘，游戏过程中不打扰。右键托盘图标可快速恢复窗口或退出。

<p align="center">
  <img src="docs/images/05_close_prompt.png" alt="关闭提示截图" /><br />
  <small><u>关闭提示</u></small>
</p>

- 托盘图标显示在线状态
- 点击关闭按钮时弹出确认对话框，提醒"退出程序会停止检测游戏"

---

## 常见问题 FAQ 🧐

**Q：我会因为使用黑金古刀而被封号吗 😨？**

本程序仅读取游戏日志文件（Player.log / CCMini 语音日志），不对游戏文件、内存进行任何修改或注入，因此极大概率不会被封号，但并不保证一定不会封号。

**Q：为什么战绩查询不到 / 数据更新有延迟？**

战绩数据来源于 https://naraka.drivod.top/ 提供的相同 API 接口，由大佬 craftwyrd 提供。程序只负责展示数据，如果遇到数据查询不到或更新延迟，原因基本出在 API 服务器本身，与本程序大概率没啥关系~ 数据相关问题可在数据问题反馈群中提问，或直接联系 API 作者 craftwyrd。

**Q：为什么安装包/程序这么大？**

程序采用**自包含发布（self-contained）**，内置了 .NET 运行时，无需用户额外安装 .NET 环境即可直接运行。加上配套的原生库，这部分占了安装包的大部分体积。

**Q：如果被杀毒软件提示怎么办？**

因为该程序没有被签名过，所以可能会被 360 等程序识别为病毒或者其他。可以关闭杀毒软件后重新打开。

**Q：在线更新失败怎么办？**

更新器（BlackGoldAncientSword.Update.exe）独立于主程序运行，常见失败原因：网络无法访问 GitHub、安装目录权限不足、杀毒软件拦截覆盖。可在 Releases 页面直接下载安装包手动覆盖安装。

---

## 免责声明 📢

BlackGoldAncientSword（黑金古刀）未经 24 Entertainment 或网易认可，不代表 24 Entertainment、网易或任何官方参与制作或管理《永劫无间》产品的人的观点或意见。《永劫无间》及其所有关联产物均为 24 Entertainment / 网易的商标或注册商标。

---

## 套盾环节 🛡️

本程序为在 GitHub 仓库 [ViewSuSu/BlackGoldAncientSword](https://github.com/ViewSuSu/BlackGoldAncientSword) 开源的代码，以及在 Release 或官方 QQ 群组中上传的二进制文件。本环节旨在让用户更加全面详尽地了解本程序以及可能的风险，以便用户在使用本程序前及过程中做出充分的风险评估和明智的决策。

本程序的目的是通过为游戏玩家提供游戏外辅助功能（战绩查询、队伍信息识别等），从而给玩家提供更好的游戏体验。我们不鼓励不支持任何违反 24 Entertainment 及网易规定或任何可能导致游戏环境不公平的行为。

本程序通过读取游戏日志文件（Player.log / CCMini 语音日志）来实现功能，其代码与行为均不含任何侵入性手段，因此在理论上并不会做出任何破坏客户端以及游戏完整性的行为，包括但不限于客户端文件内容的修改或游戏进程内存的读写等。

我们尽力保证本程序软件本体以及使用时游戏客户端的稳定性，但尽管如此，在具体的游戏环境以及官方服务更新的过程中（如反作弊系统或其他保护手段的更新），使用本程序可能会对您的游戏体验产生负面影响，如游戏闪退、账号封禁等。

使用本程序所产生的一切后果将由您自行承担，我们不对因使用本程序而产生的任何直接或间接损失负责，用户在决定使用本程序时，应充分考虑并自行承担由此产生的所有风险和后果。

我们保留随时修改本免责声明的权利，请定期查阅此页面以获取最新信息。

在您使用本程序之前，请确保您已经详细阅读、理解并同意免责声明中的条款；同时，请遵守相关游戏规则，共同维护健康和公平的游戏环境。


## 点个 Star 支持我们 ⭐

[![Star History Chart](https://api.star-history.com/svg?repos=ViewSuSu/BlackGoldAncientSword&type=Date)](https://star-history.com/#ViewSuSu/BlackGoldAncientSword&Date)

## 反馈与交流

- **客户端问题反馈QQ群**：
  - ①群：146088141
- **数据问题反馈QQ群**（QQ 群机器人也可查战绩）：
  - ①群：476074617
  - ②群：649891198
  - ③群：966720321
  - QQ 等级超过 32（两个太阳）自动审核进群，小号不予通过
- **网页端**：https://naraka.drivod.top/

---

<br>
<br>
<br>

# 开发者文档

## 解决方案概览

`src/BlackGoldAncientSword.slnx` 共包含 **9 个项目**：7 个类库 + 2 个可执行程序（主程序 App、独立更新器 Update；离线下载器 Downloader 独立于解决方案单独发布）。

```
┌────────────────────────────────────────────────────────┐
│             BlackGoldAncientSword.App                  │  ← WPF 主程序入口（WinExe）
│             (Shell / MainWindow / Tray)                │
└──────────┬─────────────────────────────────────────┬───┘
           │ 启动外部进程                              │
           ▼                                          │
┌──────────────────────────┐                          │
│ BlackGoldAncientSword.   │                          │
│ Update (独立更新器,WinExe)│                          │
│ 下载/解压/覆盖/重启       │                          │
└──────────────────────────┘                          │
                                                      │
┌──────────────────────────┐  ← 独立发布，非主程序运行时依赖
│ BlackGoldAncientSword.   │
│ Downloader (离线下载器,   │  Gitee 分卷安装包下载 →
│ WinExe)                  │  拉起 Setup 完成安装
└──────────────────────────┘
                                                      │
        ┌─────────────────────┬──────────────────────┘
        │                     │
        ▼                     ▼                     ▼
┌────────────┐        ┌──────────────┐        ┌───────────┐
│  Modules   │        │  Framework   │        │ Resources │
│ (13 个 UI  │ ◄────► │ (Core + 18   │ ◄──────│ (多语言   │
│  页面模块) │        │ 个服务接口)  │        │  XAML+图) │
└─────┬──────┘        └──────┬───────┘        └───────────┘
      │                      │
      │             ┌────────┴───────┐
      ▼             ▼                ▼
┌──────────────┐ ┌──────────────┐ ┌────────────────────┐
│ GameMonitor  │ │  Http/Auth   │ │ Framework.         │
│ (进程/日志/  │ │ (P7 签名 +   │ │ SourceGenerator    │
│  状态机)     │ │  登录子系统) │ │ (编译期生成 HTTP)  │
└──────┬───────┘ └──────┬───────┘ └────────────────────┘
       │                │
       ▼                ▼
┌──────────────┐ ┌──────────────────┐
│ CCMini 语音  │ │ 战绩 / 队友      │
│ 日志队友识别 │ │ HTTP 数据查询    │
└──────────────┘ └──────────────────┘
```

### 项目分层

| 层 | 项目 | 输出类型 | 职责 |
|---|---|---|---|
| **主程序** | `BlackGoldAncientSword.App` | WinExe | WPF 应用入口、主窗口、侧边栏导航、托盘、启动更新器、登录 / 启动 / 更新三闸门（AuthChallenge / StartupGate / UpdateGate）具体实现 |
| **更新器** | `BlackGoldAncientSword.Update` | WinExe | 独立在线更新进程，零业务依赖（仅 HandyControl + Serilog） |
| **离线下载器** | `BlackGoldAncientSword.Downloader` | WinExe | 独立单文件 exe，从 Gitee release 顺序流式下载分卷安装包 → 拉起 Setup.exe → 自身退出。零 API 依赖（走 302 + CDN） |
| **UI 模块** | `BlackGoldAncientSword.Modules` | ClassLib | 13 个 Prism `IModule` 页面（含登录 Overlay / 打赏 / 更新日志），按需加载 |
| **核心框架** | `BlackGoldAncientSword.Framework` | ClassLib | MVVM 基类、Prism 基础设施、服务抽象与实现、HTTP API（含 P7 签名 / Auth Token / 滑块 / 微信扫码 / DTO 统一映射） |
| **游戏监控** | `BlackGoldAncientSword.GameMonitor` | ClassLib | 进程检测、Player.log 解析、CCMini 语音日志队友识别、战局状态机 |
| **资源** | `BlackGoldAncientSword.Resources` | ClassLib | 多语言 XAML 资源字典、图标、图片 |
| **源码生成** | `BlackGoldAncientSword.Framework.SourceGenerator` | Roslyn Analyzer | 编译期从 JSON 定义生成 HTTP 客户端与测试代码 |
| **测试** | `BlackGoldAncientSword.Tests` | xUnit | 游戏监控、HTTP、设置、更新流程测试 |

---

## 技术栈

| 类别 | 技术 / 库 | 用途 |
|---|---|---|
| **运行时** | .NET 10.0 (`net10.0-windows`) | 目标框架 |
| **UI** | WPF + HandyControl 3.5 | 桌面界面与控件库 |
| **主题** | 自定义 ModernTheme（青瓷竹青护眼配色） | 淡雅护眼浅绿底 + 竹青深绿点缀 + 深墨字，长时间阅读舒适 |
| **MVVM 框架** | Prism 8.1 (`Prism.DryIoc`) | DI 容器、区域导航、模块化 |
| **HTTP** | 编译期源码生成器 + `HttpClient` + `DelegatingHandler` | 从 `api-definitions.json` 自动生成强类型 API 客户端；请求链上挂 `SignatureHandler`（P7 协议签名）与 `AuthTokenHandler`（Bearer + 401 单飞刷新） |
| **认证** | 自研滑块验证 + 微信扫码登录 + JWT + Windows DPAPI | 登录 token 加密落盘，过期前自动刷新，401 时并发单飞弹出登录 Overlay |
| **JSON** | `System.Text.Json`（含源码生成上下文） | 序列化 / 反序列化（已全量替换 Newtonsoft.Json） |
| **日志** | Serilog（File / Async sink） | 全应用统一日志（App / Update / Downloader） |
| **系统托盘** | Hardcodet.NotifyIcon.Wpf | 托盘图标与菜单 |
| **测试** | xUnit | 单元测试与集成测试 |
| **打包** | Self-Contained + PublishSingleFile | App、Updater、Downloader 均为单文件独立部署 (win-x64) |
| **安装包** | Inno Setup | 生成 `BlackGoldAncientSword-{version}-win-x64-Setup.exe`（全量 + DiskSpanning 分卷） |

---

## 目录结构

```
src/
├── BlackGoldAncientSword.App/              # WPF 主程序入口（WinExe）
│   ├── App.xaml / App.xaml.cs              # 应用入口、Prism 启动配置、启动流程编排（StartupGate → 更新检测 → UpdateGate → AuthChallenge → 导航到 Home）
│   ├── AppAssemblyMarker.cs                # 程序集定位（供 XAML 资源解析）
│   ├── Services/                           # App 层三闸门服务实现（依赖 IRegionManager / UI Dispatcher，无法放在 Framework）
│   │   ├── StartupGateService.cs           # 启动遮罩 latch（true → false 单向）
│   │   ├── UpdateGateService.cs            # 更新弹窗门槛（TCS 单飞 + completed latch 兜住 Complete 先于 WaitAsync 到达的竞态）
│   │   └── AuthChallengeService.cs         # 401 并发单飞：任意个后台请求撞 401 只弹一次登录 Overlay
│   └── Shell/
│       ├── MainWindow.xaml(.cs)            # 主窗口（侧边栏 + 导航 + 托盘 + 头像 Popup + 启动遮罩层）
│       ├── MainWindowViewModel.cs          # 导航命令、游戏状态、更新检测、用户信息
│       ├── UserProfileViewModel.cs         # 头像 Popup 的会员信息展示
│       ├── RegistrationExtensions.cs       # App 层类型注册扩展
│       └── ToastQueueManager.cs            # Toast 消息队列管理
│
├── BlackGoldAncientSword.Update/           # 独立在线更新器（WinExe，零业务依赖）
│   ├── App.xaml(.cs)                       # 入口：解析 --url / --target / --main-exe
│   ├── Infrastructure/ProcLog.cs           # 进程日志输出
│   ├── Services/
│   │   ├── UpdateOptions.cs                # 命令行参数模型
│   │   └── UpdaterRunner.cs                # 编排：下载→解压→关主程序→覆盖→重启
│   ├── Shell/UpdateWindow.xaml(.cs)        # 进度窗口
│   └── ViewModels/UpdateViewModel.cs       # 进度与状态绑定
│
├── BlackGoldAncientSword.Downloader/       # 离线安装包下载器（WinExe，独立单文件）
│   ├── App.xaml(.cs)                       # 入口，进程级兜底清理临时目录
│   ├── Converters/BoolToVisibilityConverter.cs
│   ├── Infrastructure/ProcLog.cs           # 进程日志输出
│   ├── Services/
│   │   ├── DownloaderRunner.cs             # 编排：查 Gitee latest → 顺序流式下载分卷 → 拉起 Setup
│   │   ├── GiteeAssetsFetcher.cs           # 走 302 + CDN，零 Gitee API 依赖（避 rate limit）
│   │   └── InstallerForegrounder.cs        # 拉起主 Setup exe 并前台化
│   ├── Shell/DownloadWindow.xaml(.cs)      # 下载进度窗口（进度 + 4-stat 实时刷新）
│   └── ViewModels/DownloadViewModel.cs
│
├── BlackGoldAncientSword.Framework/        # 核心框架
│   ├── Core/
│   │   ├── Attributes/                     # ComponentAttribute（DI 自动注册标记）
│   │   ├── Bases/                          # PrismApplicationBase、ViewModelBase、UserControlBase
│   │   ├── Consts/                         # GlobalConstant、PageNames、NavigationParameterKeys
│   │   ├── Events/                         # GameStatus、GameStatusChangedEventArgs、SettingsChangedEvent、TipMessageEvent、OnlineUpdatingStartedEvent、OnlineUpdatingCancelledEvent
│   │   ├── Extensions/                     # 容器注册扩展、UrlToImageSourceConverter、数值转换器、PageTransitionBehavior 等
│   │   └── Infrastructure/                 # IMainContentNavigationService / MainContentNavigator、AppLog / DiagLog、SearchDebounceGate / TrailingDebouncer
│   ├── Http/
│   │   ├── Definitions/
│   │   │   ├── api-definitions.json        # API 端点 / 请求 / 响应定义（→ 源码生成）
│   │   │   └── enums.json                  # 枚举定义
│   │   ├── Auth/                           # 认证子系统（自研 P7 签名 + 滑块 + 微信扫码 + JWT + DPAPI）
│   │   │   ├── ApiSignature/               # P7 请求签名：SignatureHandler / RequestSigner / ISignatureTicketProvider
│   │   │   ├── Captcha/                    # AJ 滑块验证：AjCaptchaService + AesEcbCipher
│   │   │   ├── WechatQr/                   # 微信扫码登录轮询：WechatQrLoginService
│   │   │   ├── Token/                      # Bearer token 生命周期：AuthTokenHandler（DelegatingHandler + 401 单飞刷新）+ AuthTokenState + AuthTokenRefresher + JwtExpiryReader + AuthTokenExpiryMonitor + DpapiAuthTokenStore（Windows DPAPI CurrentUser 加密落盘）
│   │   │   ├── MemberProfile/              # 会员信息查询（头像 Popup 用）
│   │   │   └── SignedOnlyHttpClient.cs     # 只挂签名不挂 Bearer 的 HttpClient，专供登录期 API（避免 AuthTokenHandler 递归 401 拦截）
│   │   ├── Unified/                        # DTO 统一映射层：把不同 API 的 PlayerStats / Season / RecentBattle / BattleDetail 归一化为 UnifiedXxx，供 UI 层无差别消费
│   │   ├── JsonFlexibleStringConverter.cs  # System.Text.Json 容错转换器
│   │   └── NarakaApiException.cs           # API 异常类型
│   ├── Services/
│   │   ├── Abstractions/                   # 18 个服务接口（见下表）
│   │   └── Implementation/                 # 服务实现（部分接口在 App 层实现）
│   ├── Themes/
│   │   ├── Generic.xaml                    # HandyControl 主题
│   │   └── ModernTheme.xaml                # 自定义青瓷竹青护眼主题
│   └── UI/Controls/                        # 自定义 WPF 控件（DataGridWrapPanel、SeasonFilterBar、FontScaleSlider、OverlayHost、TeamOverlayWindow 等）
│
├── BlackGoldAncientSword.Framework.SourceGenerator/  # Roslyn 源码生成器
│   ├── ApiDefinitionsParser.cs             # 解析 api-definitions.json
│   ├── EnumSourceGenerator.cs              # 生成枚举类型
│   ├── HttpApiSourceGenerator.cs           # 生成 NarakaApiClient + DTO（Client 模式）
│   └── HttpApiTestSourceGenerator.cs       # 生成 HTTP API 测试代码（Tests 模式）
│
├── BlackGoldAncientSword.Modules/          # UI 页面模块（13 个 Prism IModule）
│   ├── Mappings/BattleMappingRegister.cs   # 战绩 DTO 映射注册
│   ├── Module/                             # 13 个 IModule 注册
│   │   ├── AnnouncementModule.cs           # 公告
│   │   ├── AuthChallengeModule.cs          # 登录 Overlay（滑块 → 微信扫码状态机）
│   │   ├── BattleDetailModule.cs           # 对局详情浮层（personal/team/top5 三 Tab）
│   │   ├── ClosePromptModule.cs            # 关闭确认弹窗
│   │   ├── FeedbackModule.cs               # 意见反馈
│   │   ├── HomeModule.cs                   # 首页（游戏状态监控）
│   │   ├── SearchModule.cs                 # 搜索历史
│   │   ├── SettingsModule.cs               # 设置
│   │   ├── SponsorModule.cs                # 打赏支持
│   │   ├── StatsModule.cs                  # 战绩查询（含 350ms 搜索防抖）
│   │   ├── TeamInfoModule.cs               # 队伍信息（语音日志识别 + 对比）
│   │   ├── UpdateLogModule.cs              # 更新记录
│   │   └── UpdateNotificationModule.cs     # 新版本提示 / 启动更新器 / 拉取 release notes
│   └── UI/                                 # 各模块的 ViewModels + Views
│       ├── AuthChallenge/                  # 登录页：状态机 Loading→CaptchaPending→CaptchaVerifying→QrLoading→QrPolling→Success/Failed
│       ├── BattleDetail/                   # 对局详情：并行拉 personal/team/top5
│       ├── Stats/Services/                 # 战绩聚合服务（PlayerStatsLoader / BattleListLoader）
│       ├── TeamInfo/Services/              # TeamMemberLoader / PlayerStatsLoader / MockTeamData
│       └── UpdateNotification/ViewModels/  # 拉起 BlackGoldAncientSword.Update.exe / 通过 IReleaseNotesFetcher 展示 release notes
│
├── BlackGoldAncientSword.GameMonitor/      # 游戏监控
│   ├── Models/                             # BattleEventArgs、CcMiniTeammatesEventArgs、PlayerPrefsData
│   ├── Services/
│   │   ├── Abstractions/                   # IGameLogMonitor / IGameStatusMonitor / IPlayerPrefsService / ICcMiniTeammateMonitor
│   │   └── Implementation/
│   │       ├── GameLogMonitor.cs           # facade（编排生命周期与事件分发）
│   │       ├── GameStatusMonitor.cs        # 游戏状态状态机
│   │       ├── CcMiniTeammateMonitor.cs    # 解析 CCMini 语音日志 set-uid-vol 识别队友 UID
│   │       ├── PlayerPrefsService.cs       # 本地用户偏好
│   │       └── Internal/
│   │           ├── BattleStateMachine.cs   # 战局状态机
│   │           ├── GameInstallLocator.cs   # 从注册表 / 进程 exe 定位游戏安装目录
│   │           ├── LogPoller.cs            # 轮询循环
│   │           └── LogReader.cs            # Player.log 读取
│   └── GameMonitorAutoRegister.cs          # 服务自动注册
│
├── BlackGoldAncientSword.Resources/        # 多语言资源
│   ├── Images/                             # UI 图片、应用图标 (app.ico)
│   └── Themes/
│       ├── Strings.zh-CN.xaml              # 简体中文
│       ├── Strings.en.xaml                 # English
│       └── Strings.zh-TW.xaml              # 繁體中文
│
└── BlackGoldAncientSword.Tests/            # 测试项目（xUnit + Moq）
    ├── GameMonitor/                        # 游戏监控测试
    ├── Http/                               # HTTP / JSON 容错测试（含 Auth 子目录）
    ├── Settings/                           # 设置同步测试
    ├── Update/                             # 更新流程测试
    └── TestData/                           # 测试数据
```

---

## Framework 服务接口一览

`BlackGoldAncientSword.Framework/Services/Abstractions/` 下共 18 个公开接口：

| 接口 | 主要实现 | 位置 | 用途 |
|---|---|---|---|
| `IAppAssemblyMarker` | `AppAssemblyMarker` | App | 程序集定位标记（XAML 资源解析） |
| `IApplicationLifetime` | `WpfApplicationLifetime` | Framework | 退出 / 重启应用 |
| `IAuthChallengeService` | `AuthChallengeService` | App | 401 时并发单飞弹出登录 Overlay，等所有 await 者一同 resume（依赖 `IRegionManager` / `IModuleManager` / `IUpdateGateService`） |
| `IClipboardService` | `WpfClipboardService` | Framework | 剪贴板读写 |
| `IGiteeReleaseService` | `GiteeReleaseService` | Framework | 拉取 Gitee Releases 列表与资产（含 302 tag 探测 + CDN 分卷 HEAD 探测，零 API 依赖） |
| `IImageCacheService` | `ImageCacheService` | Framework | 图片磁盘缓存 |
| `ILocalizationService` | `LocalizationService` | Framework | 动态切换语言（重载 XAML 资源字典） |
| `ILocalizedTextProvider` | `WpfLocalizedTextProvider` | Framework | 代码侧读取本地化字符串 |
| `IReleaseNotesFetcher` | `GiteeReleaseNotesFetcher` | Framework | 拉取 Gitee release 描述（tag body），走网页 302 而非 `/api/v5`，避免未鉴权 IP 命中 60 req/min 限流 |
| `ISearchHistoryService` | `SearchHistoryService` | Framework | 搜索历史持久化 |
| `ISettingsService` | `SettingsService` | Framework | 应用配置读写 |
| `IStartupGateService` | `StartupGateService` | App | 启动期"检测更新未完成前禁止一切 UI 操作"的 latch（Shell 显示 → `CheckForUpdatesAsync` 返回之间遮罩整个 UI，`Complete` 只允许调用一次） |
| `ITeamOverlayService` | `TeamOverlayService` | Framework | 英雄选择时的右下角队伍弹窗 |
| `ITipMessageService` | `TipMessageService` | Framework | 全局 Toast / 提示消息 |
| `IUIDispatcher` | `WpfUIDispatcher` | Framework | 跨线程 UI 调度封装 |
| `IUiScaleService` | `UiScaleService` | Framework | 全局字号缩放（设置页字号滑块） |
| `IUpdateGateService` | `UpdateGateService` | App | 启动期"发现新版本"门槛：`AuthChallengeService.ShowAsync` 前先 `WaitAsync`，用户在更新弹窗做出任意选择后 `Complete` 才继续流程 |
| `IUpdateService` | `UpdateService` | Framework | 比对版本、解析最新 Gitee release 的 Setup / zip / 分卷 URL（走 302 + CDN，避 API rate limit） |

三个 `*Gate*` / `AuthChallenge` 接口的实现放在 `App/Services/` 而非 `Framework/Services/Implementation/`，因为它们需要 `IRegionManager` / UI Dispatcher 等只有主程序才有的运行时依赖。

`GameMonitor` 暴露自身的接口（`IGameLogMonitor` / `IGameStatusMonitor` / `IPlayerPrefsService` / `ICcMiniTeammateMonitor`），通过 `GameMonitorAutoRegister.cs` 注册到 DI 容器。

`Framework/Http/Auth/` 与 `Framework/Http/Unified/` 另外暴露一批认证与 DTO 接口，例如 `ISignatureTicketProvider`、`ISignedOnlyHttpClient`、`IAjCaptchaService`、`IWechatQrLoginService`、`IAuthTokenStore`、`IAuthTokenState`、`IAuthTokenRefresher`、`IMemberProfileService` 等，通过 `[Component]` 特性自动注册到 DI 容器。

---

## 核心模块说明

### 1. MVVM 架构（Prism + DryIoc）

- 所有 ViewModel 继承自 `ViewModelBase`，提供 `RaisePropertyChanged()` 方法（遵循 [CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md) 规范，禁止 `SetProperty` 封装）
- 属性变更通知使用 `nameof()` 或 `[CallerMemberName]`，禁止硬编码属性名字符串
- ViewModel 中**禁止引用 WPF 类型**（`Visibility`、`Brush`、`Color` 等）；可见性用 `bool` + Converter 表达
- 页面通过 `IMainContentNavigationService` 进行导航，支持前进 / 后退
- 跨模块通信使用 `IEventAggregator` 发布 / 订阅事件（如 `TipMessageEvent`、`SettingsChangedEvent`）

### 2. 模块化按需加载

13 个 UI 页面分别是一个 Prism `IModule`，在 `ModuleCatalogConfigManager` 中通过反射扫描 `IModule` 类型注册为 `OnDemand`：首次导航到某页面时才加载对应模块，减少启动时间。

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
    public const string SponsorPage            = nameof(SponsorPage);
    public const string UpdateLogPage          = nameof(UpdateLogPage);
    public const string TestTrioPage           = nameof(TestTrioPage);
    public const string TestDuoPage            = nameof(TestDuoPage);
}
```

### 3. 游戏状态监控（GameMonitor）

`GameLogMonitor` 采用 **FileSystemWatcher + 定时轮询双保险** 监听 Player.log 文件变更。内部将职责拆分为：

- `LogReader`（文件读取）
- `LogPoller`（轮询循环）
- `BattleStateMachine`（战局状态机）

外层 `GameLogMonitor` 仅作为 facade 编排生命周期与事件分发。监测到的事件：

- `BattleJoined` — 进入英雄选择（解析日志中的 RoomId）
- `BattleStarted` — 对局开始（解析 BattleId）
- `BattleEnded` — 对局结束

`GameStatusMonitor` 维护游戏状态机，通知各页面当前处于哪个阶段（`HeroSelection` / `InGame` / `BattleEnded`）。`HomePageViewModel` 额外使用 `Process.GetProcessesByName("NarakaBladepoint")` 检测进程是否存在，作为辅助判断。

### 4. CCMini 语音日志队友识别（队伍信息）

队伍识别**不依赖截屏 / OCR**，而是解析 CCMini 语音日志：永劫无间每次启动会新建 `ccmini\ccmini_new\logs\m*.log` 语音日志，进入对局（英雄选择阶段）连上队伍语音频道后，会立即写入若干行 `set-uid-vol`（为每个队友设置单独音量），其中的 `uid` 即队友角色 ID，且该记录实时落盘、无需等游戏结束。

识别流程：

1. `GameStatusMonitor` 检测到 `HeroSelection` 状态
2. `TeamInfoPageViewModel` 启动 `ICcMiniTeammateMonitor`
3. `CcMiniTeammateMonitor` 定位 CCMini 日志目录（优先注册表解析 Steam / 网易客户端路径，回退进程 exe 推导，取"最近活跃"客户端）→ 跟踪最新 `m*.log` → 增量读取并解析 `set-uid-vol` 的 uid → 排除本地用户、去重，按最近活跃排序
4. 数量达到期望阈值（三排 2 名 / 双排 1 名队友）后触发 `TeammatesReady` 事件
5. `TeamMemberLoader` 用 UID 精确命中查询每个队友（及本地用户）的战绩，按本地用户居中排列并排展示，含与本地玩家的差值 diff
6. 进入对局后持续监听 `set-uid-vol` 增量，队友退出 / 换人时实时更新卡片；本局结束才停止

### 5. HTTP API 源码生成

API 客户端**不手写**，而是通过 `BlackGoldAncientSword.Framework.SourceGenerator` 在编译期从 `Http/Definitions/*.json` 自动生成：

- JSON 定义文件描述 API 的端点、请求 / 响应数据结构与枚举
- 生成器以 **Roslyn Source Generator** 形式工作，被 Framework / Tests 两端以 Analyzer 引用
- 通过 `BgaSourceGenMode` 属性切换产物：
  - `Client` 模式（Framework）— 生成 `NarakaApiClient` + DTO
  - `Tests` 模式（Tests）— 生成 HTTP API 测试代码

### 6. 多语言支持

多语言通过 WPF `ResourceDictionary` 实现，所有 UI 文本定义在 `Strings.{zh-CN,en,zh-TW}.xaml` 中。运行时通过 `ILocalizationService.ApplyLanguage()` 动态切换资源字典，无需重启。

### 7. 在线更新（独立 Updater 进程）

更新由两端协作完成：

- **主程序侧**（`Modules/UI/UpdateNotification/ViewModels/UpdateNotificationPageViewModel.cs`）
  - 通过 `IUpdateService`（走 Gitee release 网页 302 提取 tag + CDN HEAD 探测分卷）检测新版本
  - 通过 `IReleaseNotesFetcher` 从 tag 页面抓 body（同样走 302，避 `/api/v5` 限流），在弹窗中展示 release notes
  - 用户点击"在线更新"后，启动同目录下的 `BlackGoldAncientSword.Update.exe`，传入 `--url <zip 下载地址>`、`--target <安装目录>`、`--main-exe BlackGoldAncientSword.App.exe`
  - 无网 / GitHub 被墙用户可从 Gitee Release 页面下载 `BlackGoldAncientSword-win-x64-Downloader.exe`（离线下载器），双击后自动流式拉取分卷安装包并调起 Setup
- **更新器侧**（`BlackGoldAncientSword.Update`）
  - 独立进程，不引用任何业务项目（仅依赖 HandyControl + Serilog），避免 DLL 被锁定影响整目录覆盖
  - `UpdaterRunner` 编排：下载 zip（0–90%）→ 解压（90–98%）→ 提示关闭主程序 → 全量覆盖 → 重新拉起主程序 → 自身退出
  - 以 self-contained + `PublishSingleFile` + `EnableCompressionInSingleFile` 发布

### 8. 认证与启动流程三闸门

启动流程被三个 latch/gate 串行编排，避免竞态：

```text
Shell 显示
   │
   ▼
StartupGate (IsBusy=true, 整个 UI 遮罩不可交互)
   │
   ▼
CheckForUpdatesAsync (拉 Gitee latest release + 拉 release notes)
   │
   ▼
StartupGate.Complete()   ← 无论成功 / 失败 / 异常，都要打一次，遮罩才会消失
   │
   ▼
if 有新版本 → 导航到 UpdateNotificationPage 弹窗
              用户操作后 UpdateGate.Complete()
              │
              ▼
AuthChallengeService.ShowAsync (await UpdateGate.WaitAsync 先)
   │
   ▼
若本地无有效 token → 弹 AuthChallengePage
     ├── Loading → 拉滑块题
     ├── CaptchaPending / CaptchaVerifying → AjCaptchaService.SolveAsync（AES-ECB 加密提交轨迹）
     ├── QrLoading → 拉取微信二维码
     ├── QrPolling → 每 2s 轮询扫码状态
     └── Success → AuthTokenStore 落盘（DPAPI CurrentUser 加密）
   │
   ▼
导航到 HomePage / StatsPage 等业务页面
```

**运行期 401 单飞**：任意 API 请求返回 401，`AuthTokenHandler` 会先尝试 `AuthTokenRefresher.RefreshAsync`（用 refresh_token 换新 access_token），失败则触发 `AuthChallengeService.ShowAsync`——**多个并发 401 只弹一次 Overlay**，用户完成登录后所有 await 者一同 resume 并重放原请求。

**Token 存储**：`DpapiAuthTokenStore` 用 `ProtectedData.Protect(scope=CurrentUser)` 加密后落盘到用户目录，只有同一 Windows 账户能解开；`AuthTokenExpiryMonitor` 通过 `JwtExpiryReader` 提前读到 `exp` 并在过期前 60s 主动刷新，避免请求打出去才 401。

### 9. HTTP 请求管线（P7 签名 + 401 单飞）

除了登录期专用的 `SignedOnlyHttpClient`（只挂签名不挂 Bearer），业务 HttpClient 依次经过：

```text
业务 Request
   │
   ▼
SignatureHandler        ← 从 ISignatureTicketProvider 取 ticket，按 P7 协议对 URL/Body/时间戳做签名，写入自定义 Header
   │
   ▼
AuthTokenHandler        ← 挂 Bearer token；收到 401 时先 refresh，失败则调 AuthChallengeService.ShowAsync 并等用户登录后重放请求
   │
   ▼
HttpClientHandler       ← 实际发出请求到 https://desktop.naraka.drivod.top
```

API 基地址已从 `naraka.drivod.top` 迁移到 `desktop.naraka.drivod.top`（P7 桌面端专属域名）。

### 10. Stats 搜索防抖

`StatsPageViewModel` 对搜索框输入做 350ms 防抖：用户连续敲字期间只保留最后一次触发，减少无效 API 调用（尤其是当前用户尚未登录、每次请求都要跑滑块 + 扫码时）。

---

## 构建与运行

### 环境要求

- Windows 10/11 x64
- .NET 10.0 SDK
- PowerShell 7+（推荐，UTF-8 环境）

### 构建命令

```powershell
# 还原 + 编译整个解决方案
dotnet build src/BlackGoldAncientSword.slnx

# 仅编译主程序
dotnet build src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Debug

# Release 发布主程序（自包含单文件 exe）
dotnet publish src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj -c Release -o publish/App

# Release 发布更新器（自包含单文件 exe，需与主程序放同目录）
dotnet publish src/BlackGoldAncientSword.Update/BlackGoldAncientSword.Update.csproj -c Release -o publish/Updater

# Release 发布离线下载器（自包含单文件 exe，作为 Release 独立附件发布，不进主程序安装目录）
dotnet publish src/BlackGoldAncientSword.Downloader/BlackGoldAncientSword.Downloader.csproj -c Release -o publish/Downloader
```

> 项目约定：对代码做任何修改后必须运行 `dotnet build src/BlackGoldAncientSword.slnx`，0 error 才算完成。

### 运行测试

```powershell
dotnet test src/BlackGoldAncientSword.Tests/BlackGoldAncientSword.Tests.csproj
```

---

## CI / 发布流程

`.github/workflows/` 下共两个工作流：

| 工作流 | 触发 | 用途 |
|---|---|---|
| `main-build.yml` | push / PR → `main` | 校验性构建（`dotnet build src/BlackGoldAncientSword.slnx`），不发布 |
| `dotnet-desktop.yml` | push → `release` | 完整发版：版本号自增 → 编译 App → 发布 App + Updater + Downloader（自包含单文件）→ 打包 zip + Inno Setup 全量 / 分卷安装包 → 创建 GitHub Release |

发版流程要点：

1. 从已有 git tags（`v*.*.*.*` 形式）推断版本号并自增 build 段
2. 修改 `App.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion`
3. 分别发布 App、Update、Downloader 为自包含单文件 .exe
4. 合并 build + publish 输出并把 Updater exe 一并塞进安装目录 → 压缩为 `BlackGoldAncientSword-v{version}.zip` + 7z 分卷 `-split.zip.NNN`（≤99MB / 卷）
5. 用 `setup.iss`（Inno Setup 脚本）分别生成全量安装包 `BlackGoldAncientSword-{version}-win-x64-Setup.exe` 与 DiskSpanning 分卷安装包 `-Split.exe` + `.bin`
6. 额外产出两份无版本号别名（`BlackGoldAncientSword-win-x64-Setup.exe` / `-Downloader.exe`），配合 `/releases/latest/download/` magic redirect 提供永久指向最新版的分享链接
7. 创建 GitHub Release，自动列举上一版本到本次的 commit 标题

> `release` 分支已开启分支保护：禁直推 / 禁 force-push / 禁删除，且对管理员同样生效（`enforce_admins`），所有变更必须通过 PR 合并进来。日常开发在 `main` 分支进行；发版时先由 [commit-as-xiaochuang](.claude/skills/commit-as-xiaochuang/SKILL.md) skill commit + push `main`，随后在 GitHub 上创建 `main` → `release` 的 PR 并合并，由此触发 `dotnet-desktop.yml` 完成发版。

---

### 特别鸣谢

- 微信号：craftwyrd

---

### 许可证

本项目基于 [MIT License](LICENSE) 开源。作者：**小窗同学**。
