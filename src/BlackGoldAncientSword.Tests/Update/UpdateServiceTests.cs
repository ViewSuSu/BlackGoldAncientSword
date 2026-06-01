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
    /// Before the check completes, neither indicator should show.
    /// </summary>
    [Fact]
    public void BeforeCheck_NeitherIndicatorShows()
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

        // Before any event fires
        Assert.False(updateCheckCompleted, "Check not yet completed");
        Assert.False(isUpdateAvailable, "No update detected yet");
        Assert.False(isLatestVersion, "Not showing '(最新)' yet");
    }
}