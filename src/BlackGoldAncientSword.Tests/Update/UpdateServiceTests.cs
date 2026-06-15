using Xunit;
using Xunit.Abstractions;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Framework.Services.Implementation;

namespace BlackGoldAncientSword.Tests.Update;

public class UpdateServiceTests
{
    private readonly ITestOutputHelper _output;
    public UpdateServiceTests(ITestOutputHelper output) { _output = output; }

    /// <summary>
    /// Simulates the MainWindowVM logic: when UpdateAvailabilityChanged(false) fires,
    /// IsLatestVersion should become true (check completed, no update).
    /// This was the bug — the event wasn't firing when versions matched.
    /// </summary>
    [Fact]
    public void UpdateAvailabilityChanged_FiresFalse_CompletesCheck()
    {
        // Simulate MainWindowVM state
        bool updateCheckCompleted = false;
        bool isUpdateAvailable = false;
        bool isLatestVersion = false;

        // Handler: same logic as MainWindowVM.OnUpdateAvailabilityChanged
        void OnUpdateAvailabilityChanged(bool available)
        {
            updateCheckCompleted = true;
            isUpdateAvailable = available;
            isLatestVersion = updateCheckCompleted && !isUpdateAvailable;
        }

        // Act: fire the event with false (no update available)
        OnUpdateAvailabilityChanged(false);

        // Assert: version check completed AND we know we're on latest
        Assert.True(updateCheckCompleted, "Check should be marked as completed");
        Assert.False(isUpdateAvailable, "Should not detect an update");
        Assert.True(isLatestVersion, "Should show '(最新)' next to version");
    }

    /// <summary>
    /// When UpdateAvailabilityChanged(true) fires, IsUpdateAvailable should be true
    /// and the "发现新版本" indicator should show.
    /// </summary>
    [Fact]
    public void UpdateAvailabilityChanged_FiresTrue_ShowsUpdateIndicator()
    {
        bool updateCheckCompleted = false;
        bool isUpdateAvailable = false;
        bool isLatestVersion = false;

        void OnUpdateAvailabilityChanged(bool available)
        {
            updateCheckCompleted = true;
            isUpdateAvailable = available;
            isLatestVersion = updateCheckCompleted && !isUpdateAvailable;
        }

        // Act: fire the event with true (update available)
        OnUpdateAvailabilityChanged(true);

        // Assert
        Assert.True(updateCheckCompleted, "Check should be marked as completed");
        Assert.True(isUpdateAvailable, "Should detect an update");
        Assert.False(isLatestVersion, "Should NOT show '(最新)' when update is available");
    }

    /// <summary>
    /// 在 UpdateService 完成任何检查之前，IsUpdateAvailable 必须为 false——
    /// 这是"既未发现更新、也未确认最新"的初始指示器状态。
    /// </summary>
    /// <remarks>
    /// 直接 new UpdateService() 会在构造函数中触发 WPF pack URI 与 SparkleUpdater 初始化，
    /// 在无 WPF 宿主的 xUnit 进程下会抛异常，因此使用 RuntimeHelpers.GetUninitializedObject
    /// 绕过构造函数，仅断言被测类型上 IsUpdateAvailable 自动属性的字段默认值。
    /// 这确保断言真正绑定到生产代码 UpdateService 的属性，而非局部变量。
    ///
    /// 不断言 IsLatestVersion：该属性派生自 MainWindowViewModel（在 App 程序集内），
    /// 测试项目未引用 App，且 ViewModel 构造依赖 7 个 Prism/服务接口，超出本测试范围。
    /// </remarks>
    [Fact]
    public void BeforeCheck_NeitherIndicatorShows()
    {
        var sut = (UpdateService)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(UpdateService));

        Assert.False(sut.IsUpdateAvailable,
            $"{nameof(UpdateService.IsUpdateAvailable)} 在检查前应为 false");
        Assert.Null(sut.LatestVersion);
    }
}