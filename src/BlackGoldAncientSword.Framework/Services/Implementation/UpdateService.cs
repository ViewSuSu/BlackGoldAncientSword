using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.WPF;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    /// <summary>
    /// Implements IAssemblyAccessor using reflection on the given assembly.
    /// AssemblyVersion is overridden with a normalized string matching the appcast.
    /// Avoids file-path accessors which break under single-file publish.
    /// </summary>
    internal sealed class VersionNormalizedAssemblyAccessor : IAssemblyAccessor
    {
        private readonly Assembly _assembly;

        public VersionNormalizedAssemblyAccessor(Assembly assembly, string normalizedVersion)
        {
            _assembly = assembly;
            AssemblyVersion = normalizedVersion;
        }

        public string AssemblyVersion { get; }
        public string AssemblyTitle => GetAttr<AssemblyTitleAttribute>()?.Title ?? "";
        public string AssemblyDescription => GetAttr<AssemblyDescriptionAttribute>()?.Description ?? "";
        public string AssemblyProduct => GetAttr<AssemblyProductAttribute>()?.Product ?? "";
        public string AssemblyCopyright => GetAttr<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        public string AssemblyCompany => GetAttr<AssemblyCompanyAttribute>()?.Company ?? "";

        private T? GetAttr<T>() where T : Attribute =>
            _assembly.GetCustomAttribute<T>();
    }

    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private SparkleUpdater? _sparkle;
        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public event System.EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService()
        {
            Debug.WriteLine("[UpdateService] 构造函数开始");

            CurrentVersion = GetCurrentVersion();
            Debug.WriteLine($"[UpdateService] 当前版本: {CurrentVersion}");

            var appcastUrl = GetDefaultAppcastUrl();
            Debug.WriteLine($"[UpdateService] Appcast URL: {appcastUrl}");

            // Load icon from Resources assembly
            var iconUri = new Uri("pack://application:,,,/BlackGoldAncientSword.Resources;component/Images/app.png");
            var iconImage = new BitmapImage(iconUri);
            Debug.WriteLine("[UpdateService] 图标加载完成");

            _sparkle = new SparkleUpdater(
                appcastUrl,
                new NetSparkleUpdater.SignatureVerifiers.Ed25519Checker(SecurityMode.Unsafe, "")
            )
            {
                UIFactory = new CustomUIFactory(iconImage),
                RelaunchAfterUpdate = true,
                LogWriter = new LogWriter(LogWriterOutputMode.Debug),
            };

            // Override AssemblyAccessor to use normalized version string
            var entryAsm = typeof(UpdateService).Assembly;
            if (entryAsm != null)
            {
                _sparkle.Configuration.AssemblyAccessor =
                    new VersionNormalizedAssemblyAccessor(entryAsm, CurrentVersion);
                Debug.WriteLine($"[UpdateService] 已设置自定义 AssemblyAccessor，规范化版本: {CurrentVersion}");
            }

            Debug.WriteLine("[UpdateService] SparkleUpdater 实例已创建，SecurityMode=Unsafe");

            // Update detected
            _sparkle.UpdateDetected += (_, args) =>
            {
                var latestVer = args.LatestVersion?.Version ?? "";
                Debug.WriteLine($"[UpdateService] UpdateDetected 事件触发，最新版本: {latestVer}, 当前版本: {CurrentVersion}");

                // Guard: ignore if same version is detected (e.g. assembly version differs from appcast version)
                if (string.Equals(latestVer, CurrentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[UpdateService] 版本一致 ({latestVer})，忽略更新通知");
                    return;
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsUpdateAvailable = true;
                    LatestVersion = latestVer;
                    Debug.WriteLine($"[UpdateService] 已标记 IsUpdateAvailable=true, LatestVersion={latestVer}");
                    UpdateAvailabilityChanged?.Invoke(this, true);
                    Debug.WriteLine("[UpdateService] UpdateAvailabilityChanged 事件已触发 (true)");
                });
            };

            // Check finished without finding update
            _sparkle.UpdateCheckFinished += (_, status) =>
            {
                Debug.WriteLine($"[UpdateService] UpdateCheckFinished 事件触发，状态: {status}");
                if (status != UpdateStatus.UpdateAvailable)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsUpdateAvailable = false;
                        LatestVersion = null;
                        Debug.WriteLine("[UpdateService] 已标记 IsUpdateAvailable=false, LatestVersion=null");
                        UpdateAvailabilityChanged?.Invoke(this, false);
                        Debug.WriteLine("[UpdateService] UpdateAvailabilityChanged 事件已触发 (false)");
                    });
                }
            };

            // Close app when update is ready to install
            _sparkle.CloseApplication += () =>
            {
                Debug.WriteLine("[UpdateService] CloseApplication 事件触发，正在关闭应用");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                    Debug.WriteLine("[UpdateService] Application.Shutdown 已调用");
                });
            };

            Debug.WriteLine("[UpdateService] 构造函数完成，所有事件已注册");
        }

        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdatesAsync 调用，showNoUpdateMessage={showNoUpdateMessage}");

            if (_sparkle == null)
            {
                Debug.WriteLine("[UpdateService] _sparkle 为 null，跳过检查");
                return;
            }

            if (showNoUpdateMessage)
            {
                Debug.WriteLine("[UpdateService] 用户主动检查更新 -> CheckForUpdatesAtUserRequest");
                // User-requested check: dispatch to UI thread so NetSparkle''s WPF
                // controls (ProgressBar, dialogs) are created on the correct thread.
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => _sparkle.CheckForUpdatesAtUserRequest());
                Debug.WriteLine("[UpdateService] CheckForUpdatesAtUserRequest 完成");
            }
            else
            {
                Debug.WriteLine("[UpdateService] 启动时静默检查 -> CheckForUpdatesQuietly");
                // Silent auto-check at startup
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => _sparkle.CheckForUpdatesQuietly());
                Debug.WriteLine("[UpdateService] CheckForUpdatesQuietly 完成");
            }
        }

        private static string GetCurrentVersion()
        {
            // Read AssemblyInformationalVersionAttribute, strip Git commit hash suffix
            var attr = typeof(UpdateService).Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                var version = attr.InformationalVersion;
                var plusIndex = version.IndexOf('+');
                return plusIndex > 0 ? version[..plusIndex] : version;
            }
            return "0.0.0";
        }

        private static string GetDefaultAppcastUrl()
        {
            Debug.WriteLine("[UpdateService] GetDefaultAppcastUrl 开始");

            // GitHub 仓库名是固定的，不应依赖 AssemblyProduct（会被 SDK 默认设为项目名 BlackGoldAncientSword.App）
            const string repoName = "BlackGoldAncientSword";
            var url = $"https://github.com/ViewSuSu/{repoName}/releases/latest/download/appcast.xml";

            Debug.WriteLine($"[UpdateService] Appcast URL: {url}");
            return url;
        }
    }
}