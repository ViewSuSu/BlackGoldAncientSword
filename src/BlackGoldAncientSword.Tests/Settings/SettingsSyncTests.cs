using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.ClosePrompt.ViewModels;
using BlackGoldAncientSword.Modules.UI.Settings.ViewModels;

namespace BlackGoldAncientSword.Tests.Settings
{
    /// <summary>
    /// 验证 bug 修复：
    /// "托盘菜单 / 关闭对话框改配置后，若设置页已经打开，其 UI 不刷新"。
    /// <para>
    /// 模拟"鼠标点击"策略：真实的鼠标点击最终触发的是 ViewModel 属性 setter 或 DelegateCommand 执行；
    /// 本测试直接驱动这些 setter/command，等价于点击的行为链末端，并验证 ISettingsService.SettingsChanged
    /// 事件是否让订阅方（设置页 / 关闭对话框 VM）真正刷新绑定属性。
    /// </para>
    /// </summary>
    [Collection(nameof(PrismTestCollection))]
    public class SettingsSyncTests
    {
        // ===== 测试用的 mock ISettingsService =====
        // 用 stub 类而非 Moq 事件模拟，方便调用 RaiseSettingsChanged 触发广播、以及记录 SaveAsync 次数。
        private sealed class FakeSettingsService : ISettingsService
        {
            public AppSettings Current { get; set; } = new();
            public int SaveAsyncCallCount { get; private set; }
            public int ReloadAsyncCallCount { get; private set; }
            public event EventHandler? SettingsChanged;

            public Task LoadAsync() => Task.CompletedTask;
            public Task ReloadAsync() { ReloadAsyncCallCount++; return Task.CompletedTask; }

            public Task SaveAsync()
            {
                SaveAsyncCallCount++;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            }

            /// <summary>模拟"外部改动 + 广播"：例如托盘菜单 / FileSystemWatcher 触发的场景。</summary>
            public void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ===== 同步 IUIDispatcher：CheckAccess 返回 true 让 Handler 就地执行 =====
        private sealed class SyncUIDispatcher : IUIDispatcher
        {
            public bool CheckAccess() => true;
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
            public Task InvokeAsync(Func<Task> asyncAction) => asyncAction();
            public void BeginInvoke(Action action) => action();
        }

        // ===== 最小 ILocalizationService stub =====
        private sealed class FakeLocalizationService : ILocalizationService
        {
            public string CurrentLanguage { get; set; } = "zh-CN";
            public ObservableCollection<LanguageOption> AvailableLanguages { get; } = new();
            public void ApplyLanguage(string language) { CurrentLanguage = language; }
#pragma warning disable CS0067 // ILocalizationService 接口成员，测试中未触发但必须实现
            public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
        }

        // ===== 记录属性变更的辅助方法 =====
        private static List<string> CapturePropertyChanges(INotifyPropertyChanged vm)
        {
            var list = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.PropertyName))
                    list.Add(e.PropertyName!);
            };
            return list;
        }

        private static SettingsPageViewModel BuildSettingsVm(FakeSettingsService settings)
        {
            var updateService = new Mock<IUpdateService>();
            updateService.SetupGet(u => u.CurrentVersion).Returns("test");

            return new SettingsPageViewModel(
                settings,
                new FakeLocalizationService(),
                new Mock<ILocalizedTextProvider>().Object,
                new Mock<IMainContentNavigationService>().Object,
                new Mock<IImageCacheService>().Object,
                updateService.Object,
                new Mock<IClipboardService>().Object,
                new SyncUIDispatcher());
        }

        private static ClosePromptPageViewModel BuildClosePromptVm(FakeSettingsService settings)
        {
            return new ClosePromptPageViewModel(
                settings,
                new Mock<IApplicationLifetime>().Object,
                new SyncUIDispatcher());
        }

        // ==================== 用例开始 ====================

        /// <summary>
        /// 场景：托盘菜单"关闭行为"从 MinimizeToTaskbar 切到 ExitDirectly。
        /// 等价链路：MainWindow.TrayMenu_CloseBehavior_Click -> settings.Current.CloseBehavior = "ExitDirectly" -> SaveAsync。
        /// 期望：已打开的设置页 VM 收到 SettingsChanged，触发 SelectedCloseBehavior 的 PropertyChanged。
        /// bug 修复前：设置页 VM 只在 ctor / OnNavigatedTo 读一次，托盘改动无法反映到 UI。
        /// </summary>
        [Fact]
        public async Task TrayMenu_ChangeCloseBehavior_SettingsPageUiRefreshes()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehavior = "MinimizeToTaskbar";

            var vm = BuildSettingsVm(settings);
            var changes = CapturePropertyChanges(vm);

            // 模拟托盘点击后的行为：改内存 + SaveAsync（等价于 MainWindow.xaml.cs:467 TrayMenu_CloseBehavior_Click）
            settings.Current.CloseBehavior = "ExitDirectly";
            await settings.SaveAsync();

            Assert.Contains(nameof(SettingsPageViewModel.SelectedCloseBehavior), changes);
            Assert.Equal("ExitDirectly", vm.SelectedCloseBehavior);
        }

        /// <summary>
        /// 场景：托盘"记住关闭行为"切换。
        /// 期望：设置页 RememberCloseBehavior PropertyChanged 触发，值同步。
        /// </summary>
        [Fact]
        public async Task TrayMenu_ToggleRemember_SettingsPageUiRefreshes()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = false;

            var vm = BuildSettingsVm(settings);
            var changes = CapturePropertyChanges(vm);

            settings.Current.CloseBehaviorRemembered = true;
            await settings.SaveAsync();

            Assert.Contains(nameof(SettingsPageViewModel.RememberCloseBehavior), changes);
            Assert.True(vm.RememberCloseBehavior);
        }

        /// <summary>
        /// 场景：托盘"团队 Overlay 显示"切换。
        /// </summary>
        [Fact]
        public async Task TrayMenu_ToggleTeamOverlay_SettingsPageUiRefreshes()
        {
            var settings = new FakeSettingsService();
            settings.Current.ShowTeamOverlayDuringHeroSelection = true;

            var vm = BuildSettingsVm(settings);
            var changes = CapturePropertyChanges(vm);

            settings.Current.ShowTeamOverlayDuringHeroSelection = false;
            await settings.SaveAsync();

            Assert.Contains(nameof(SettingsPageViewModel.ShowTeamOverlayDuringHeroSelection), changes);
            Assert.False(vm.ShowTeamOverlayDuringHeroSelection);
        }

        /// <summary>
        /// 场景：外部编辑器改写 settings.json（等价 FileSystemWatcher 触发 ReloadFromWatcher 后广播）。
        /// 期望：设置页多个属性一齐刷新。
        /// </summary>
        [Fact]
        public void ExternalFileEdit_SettingsPageAllPropertiesRefresh()
        {
            var settings = new FakeSettingsService();
            var vm = BuildSettingsVm(settings);
            var changes = CapturePropertyChanges(vm);

            // 模拟 Watcher Reload：Current 被替换 + 广播
            settings.Current = new AppSettings
            {
                DataSavePath = @"D:\NewData",
                CachePath = @"D:\NewCache",
                CloseBehavior = "ExitDirectly",
                CloseBehaviorRemembered = true,
                Language = "en-US",
                ShowTeamOverlayDuringHeroSelection = false,
            };
            settings.RaiseSettingsChanged();

            Assert.Contains(nameof(SettingsPageViewModel.DataPath), changes);
            Assert.Contains(nameof(SettingsPageViewModel.CachePath), changes);
            Assert.Contains(nameof(SettingsPageViewModel.SelectedCloseBehavior), changes);
            Assert.Contains(nameof(SettingsPageViewModel.RememberCloseBehavior), changes);
            Assert.Contains(nameof(SettingsPageViewModel.SelectedLanguage), changes);
            Assert.Contains(nameof(SettingsPageViewModel.ShowTeamOverlayDuringHeroSelection), changes);
            Assert.Equal(@"D:\NewData", vm.DataPath);
            Assert.Equal(@"D:\NewCache", vm.CachePath);
        }

        /// <summary>
        /// 场景：关闭对话框上"记住选项"复选框勾选。
        /// 期望：Current.CloseBehaviorRemembered 变为 true 且 SaveAsync 调用（严格实时同步落盘）。
        /// bug 修复前：只有点击"最小化/退出"按钮时才落盘。
        /// </summary>
        [Fact]
        public void ClosePromptDialog_CheckRemember_PersistsImmediately()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = false;

            var vm = BuildClosePromptVm(settings);

            // 等价于鼠标点击复选框：CheckBox IsChecked 绑定触发 setter
            vm.RememberChoice = true;

            Assert.True(settings.Current.CloseBehaviorRemembered);
            Assert.Equal(1, settings.SaveAsyncCallCount);
        }

        /// <summary>
        /// 场景：关闭对话框勾选 → 设置页 UI 同步刷新（双 VM 共享同一 SettingsService）。
        /// bug 修复的核心：跨界面 UI 实时一致。
        /// </summary>
        [Fact]
        public void ClosePromptCheck_SettingsPageRememberBehaviorRefreshes()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = false;

            var settingsVm = BuildSettingsVm(settings);
            var closePromptVm = BuildClosePromptVm(settings);

            var changes = CapturePropertyChanges(settingsVm);

            closePromptVm.RememberChoice = true;

            Assert.Contains(nameof(SettingsPageViewModel.RememberCloseBehavior), changes);
            Assert.True(settingsVm.RememberCloseBehavior);
        }

        /// <summary>
        /// 场景：设置页勾选 → 关闭对话框已打开时 UI 同步。反向验证双向一致性。
        /// </summary>
        [Fact]
        public void SettingsPageCheck_ClosePromptRememberChoiceRefreshes()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = false;

            var closePromptVm = BuildClosePromptVm(settings);
            var settingsVm = BuildSettingsVm(settings);

            var changes = CapturePropertyChanges(closePromptVm);

            // 模拟设置页勾选 "记住关闭行为"
            settingsVm.RememberCloseBehavior = true;

            Assert.Contains(nameof(ClosePromptPageViewModel.RememberChoice), changes);
            Assert.True(closePromptVm.RememberChoice);
        }

        /// <summary>
        /// 关键防回响用例：外部广播 → VM 刷新时不得再次触发 SaveAsync，否则事件订阅链会形成
        /// "外部改 → 广播 → VM setter → SaveAsync → 再广播 → ..." 无限循环。
        /// </summary>
        [Fact]
        public void ExternalBroadcast_DoesNotTriggerSaveAsyncAgain()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = false;

            var closePromptVm = BuildClosePromptVm(settings);
            var settingsVm = BuildSettingsVm(settings);

            // 通过"外部改 + 广播"路径（非 setter），SaveAsync 应保持 0 次
            settings.Current.CloseBehaviorRemembered = true;
            settings.RaiseSettingsChanged();

            Assert.True(closePromptVm.RememberChoice);
            Assert.True(settingsVm.RememberCloseBehavior);
            Assert.Equal(0, settings.SaveAsyncCallCount);
        }

        /// <summary>
        /// 关闭对话框 ctor 从持久化值初始化 RememberChoice，而不是每次都 false。
        /// </summary>
        [Fact]
        public void ClosePromptViewModel_InitializesRememberChoiceFromSettings()
        {
            var settings = new FakeSettingsService();
            settings.Current.CloseBehaviorRemembered = true;

            var vm = BuildClosePromptVm(settings);

            Assert.True(vm.RememberChoice);
        }

        /// <summary>
        /// Dispose 后不应再收到 SettingsChanged 回调（订阅已解绑，避免泄漏与"僵尸 VM 修改 UI"）。
        /// </summary>
        [Fact]
        public void DisposedViewModel_NoLongerRespondsToSettingsChanged()
        {
            var settings = new FakeSettingsService();
            var vm = BuildSettingsVm(settings);
            var changes = CapturePropertyChanges(vm);

            vm.Dispose();

            settings.Current.CloseBehavior = "ExitDirectly";
            settings.RaiseSettingsChanged();

            Assert.DoesNotContain(nameof(SettingsPageViewModel.SelectedCloseBehavior), changes);
        }
    }
}
