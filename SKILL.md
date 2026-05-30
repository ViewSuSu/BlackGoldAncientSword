---
name: naraka-stats-assistant
description: 永劫无间战绩助手项目技能。提供项目架构、模块组织、编码约定和开发工作流指导。当在此项目中编写、审查或重构代码时使用。
license: MIT
---

# 永劫无间战绩助手 (BlackGoldAncientSword)

基于 WPF + Prism (DryIoc) 的桌面应用，用于查询《永劫无间》(NARAKA: BLADEPOINT) 玩家战绩数据。

## 项目架构

解决方案 `BlackGoldAncientSword.slnx` 包含以下项目：

| 项目 | 职责 |
|---|---|
| `BlackGoldAncientSword.App` | 应用入口、Shell/MainWindow、启动注册 |
| `BlackGoldAncientSword.Framework` | 基础设施：Prism 基类、事件、服务抽象与实现、UI 控件、扩展转换器 |
| `BlackGoldAncientSword.Framework.SourceGenerator` | 源生成器：`EnumSourceGenerator`、`HttpApiSourceGenerator` |
| `BlackGoldAncientSword.GameMonitor` | 游戏状态监控：进程检测、日志监控、PlayerPrefs 读取 |
| `BlackGoldAncientSword.Modules` | 业务模块：Home、Search、Stats、Settings、TeamInfo、Announcement、ClosePrompt |
| `BlackGoldAncientSword.Ocr` | OCR 服务：封装 PaddleOCR-json.exe，提供文字识别 |
| `BlackGoldAncientSword.ScreenCapture` | 屏幕捕获：基于 Windows Graphics Capture (WGC) API |
| `BlackGoldAncientSword.Resources` | 静态资源：图片、主题样式 |
| `BlackGoldAncientSword.Tests` | 单元测试 |

## 核心模式

### 自动注册 (Auto-Registration)

各层通过扩展方法实现自动 DI 注册，使用 `[Component]` 特性标记：

- `RegisterFrameworkServices()` 鈥?Framework 层服务
- `RegisterAppLayer()` 鈥?App 层
- `RegisterModuleLayer()` 鈥?Modules 层
- `RegisterGameMonitorLayer()` 鈥?GameMonitor 层
- `RegisterOcrLayer()` 鈥?OCR 层
- `RegisterScreenCaptureLayer()` 鈥?ScreenCapture 层

### 模块发现

`ModuleCatalogConfigManager.ConfigAll()` 通过反射扫描 `BlackGoldAncientSword.Modules` 程序集中所有 `IModule` 实现，自动构建 Prism ModuleCatalog。

### 对象映射

使用 Mapster，映射配置在 `BattleMappingRegister` 中集中定义，通过 `TypeAdapterConfig.GlobalSettings.Scan()` 自动发现。

### 事件通信

通过 Prism `IEventAggregator` 发布/订阅，主要事件：
- `GameStatusChangedEvent` 鈥?游戏状态变更
- `SettingsChangedEvent` 鈥?设置变更
- `TipMessageEvent` 鈥?提示消息

## 编码约定

- 所有文件使用 UTF-8 编码（无 BOM）
- 命名空间遵循项目名 + 功能层级（如 `BlackGoldAncientSword.Modules.UI.Stats.ViewModels`）
- 服务接口放在 `Abstractions` 子命名空间，实现放在 `Implementation`
- ViewModel / View 命名配对，放在对应功能 UI 目录下
- 禁止 git 回滚操作（`git revert`、`git reset`、`git checkout`），除非明确要求

## 构建与运行

- .NET SDK 10.0+
- Windows 10.0.17763.0+
- OCR 依赖 `ocr_engine/` 目录下的 PaddleOCR-json 及相关 DLL

```powershell
dotnet build src/BlackGoldAncientSword.App/BlackGoldAncientSword.App.csproj
```

## 外部依赖

- Prism.DryIoc 鈥?MVVM / DI 框架
- Mapster 鈥?对象映射
- PaddleOCR-json 鈥?OCR 引擎（外部进程调用）
- Windows.Graphics.Capture 鈥?屏幕捕获
