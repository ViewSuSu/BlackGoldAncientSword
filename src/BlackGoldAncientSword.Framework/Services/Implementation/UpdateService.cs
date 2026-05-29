using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.UI.WPF;
using Prism.Events;
using System.Reflection;
using System.Threading;
using System.Windows.Media.Imaging;

namespace BlackGoldAncientSword.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class UpdateService : IUpdateService
    {
        private readonly IEventAggregator _eventAggregator;
        private SparkleUpdater? _sparkle;
        private string? _customAppcastUrl;

        public string CurrentVersion { get; }

        public bool IsUpdateAvailable { get; private set; }

        public string? LatestVersion { get; private set; }

        public event System.EventHandler<bool>? UpdateAvailabilityChanged;

        public UpdateService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            CurrentVersion = GetCurrentVersion();
        }

        public void Configure(string? customAppcastUrl = null)
        {
            _customAppcastUrl = customAppcastUrl;

            var appcastUrl = _customAppcastUrl ?? GetDefaultAppcastUrl();

            // Load icon from Resources assembly
            var iconUri = new Uri("pack://application:,,,/BlackGoldAncientSword.Resources;component/Images/app.png");
            var iconImage = new BitmapImage(iconUri);

            _sparkle = new SparkleUpdater(
                appcastUrl,
                new NetSparkleUpdater.SignatureVerifiers.Ed25519Checker(SecurityMode.Unsafe, "")
            )
            {
                UIFactory = new UIFactory(iconImage),
                RelaunchAfterUpdate = false,
            };

            // Update detected
            _sparkle.UpdateDetected += (_, args) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsUpdateAvailable = true;
                    LatestVersion = args.LatestVersion?.Version;
                    UpdateAvailabilityChanged?.Invoke(this, true);
                });
            };

            // Check finished without finding update
            _sparkle.UpdateCheckFinished += (_, status) =>
            {
                if (status != UpdateStatus.UpdateAvailable)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsUpdateAvailable = false;
                        LatestVersion = null;
                        UpdateAvailabilityChanged?.Invoke(this, false);
                    });
                }
            };

            // Close app when update is ready to install
            _sparkle.CloseApplication += () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
            };
        }
        /// <summary>
        /// Runs an action on a dedicated STA thread (required by WPF UI factories like NetSparkle's UIFactory).
        /// </summary>
        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            var thread = new Thread(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }


        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true)
        {
            if (_sparkle == null) return;

            if (showNoUpdateMessage)
            {
                // User-requested check: NetSparkle handles UI automatically
                await RunOnStaThreadAsync(() => _sparkle.CheckForUpdatesAtUserRequest());
            }
            else
            {
                // Silent auto-check at startup
                await RunOnStaThreadAsync(() => _sparkle.CheckForUpdatesQuietly());
            }
        }

        private static string GetCurrentVersion()
        {
            var entryAsm = Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var thisAssemblyType = entryAsm.GetType("ThisAssembly");
                if (thisAssemblyType != null)
                {
                    var field = thisAssemblyType.GetField("AssemblyInformationalVersion");
                    if (field != null)
                    {
                        var value = field.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var plusIndex = value.IndexOf('+');
                            return plusIndex > 0 ? value[..plusIndex] : value;
                        }
                    }
                }

                var attr = entryAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (attr != null)
                {
                    var version = attr.InformationalVersion;
                    var plusIndex = version.IndexOf('+');
                    return plusIndex > 0 ? version[..plusIndex] : version;
                }

                var fileVersionAttr = entryAsm.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (fileVersionAttr != null)
                {
                    return fileVersionAttr.Version;
                }
            }

            return "1.0.0";
        }

        private static string GetDefaultAppcastUrl()
        {
            var entryAsm = Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var productAttr = entryAsm.GetCustomAttribute<AssemblyProductAttribute>();
                var product = productAttr?.Product ?? "BlackGoldAncientSword";
                return $"https://github.com/ViewSuSu/{product}/releases/latest/download/appcast.xml";
            }

            return "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/latest/download/appcast.xml";
        }
    }
}