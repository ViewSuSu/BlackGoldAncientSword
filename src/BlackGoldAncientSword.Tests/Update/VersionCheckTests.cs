using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// 验证本地 App 程序集版本是否与远端最新 tag 版本一致。
/// 远端 tag 通过 git ls-remote 获取，本地版本通过
/// AssemblyInformationalVersionAttribute 获取（与 UpdateService.GetCurrentVersion() 逻辑一致）。
/// 网络不可用时跳过远端对比，仅输出提示。
/// </summary>
public class VersionCheckTests
{
    private static string? _repoRoot;

    private static string GetRepoRoot()
    {
        if (_repoRoot is not null)
            return _repoRoot;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                _repoRoot = dir.FullName;
                return _repoRoot;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "未找到仓库根目录（.git）。测试必须从 git 仓库中运行。");
    }

    private static string GetLocalVersion()
    {
        var attr = typeof(VersionCheckTests).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attr is null)
            return "0.0.0";

        var version = attr.InformationalVersion;
        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version[..plusIndex] : version;
    }

    private static string NormalizeTag(string tag)
    {
        if (tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V'))
            return tag[1..];
        return tag;
    }

    private static async Task<string?> GetLatestRemoteTagAsync()
    {
        var repoRoot = GetRepoRoot();

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "ls-remote --tags origin",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            var readTask = process.StandardOutput.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync(cts.Token);
            await Task.WhenAll(readTask, waitTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }

        if (process.ExitCode != 0)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        var tagPattern = @"refs/tags/(v?[\d.]+)$";
        var tagSet = new HashSet<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, tagPattern);
            if (match.Success)
                tagSet.Add(match.Groups[1].Value);
        }

        if (tagSet.Count == 0)
            return null;

        return tagSet
            .Select(t => ParseVersion(NormalizeTag(t)))
            .Max()
            ?.ToString();
    }

    private static Version ParseVersion(string versionStr)
    {
        var parts = versionStr.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        var revision = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;
        return new Version(major, minor, build, revision);
    }

    [Fact]
    public void LocalVersion_CanBeRead()
    {
        var version = GetLocalVersion();
        Assert.NotEqual("0.0.0", version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public async Task LocalVersion_ShouldNotBeBehind_LatestRemoteTag()
    {
        var localVersionStr = GetLocalVersion();
        var latestRemoteTagStr = await GetLatestRemoteTagAsync();

        if (latestRemoteTagStr is null)
        {
            // 网络不可用时跳过，不阻塞 CI 或离线开发
            Assert.True(true, "远端 tag 不可用（网络或认证问题），跳过版本对比。");
            return;
        }

        var localVersion = ParseVersion(localVersionStr);
        var remoteVersion = ParseVersion(latestRemoteTagStr);

        Assert.True(
            localVersion >= remoteVersion,
            $"本地版本 ({localVersionStr}) 低于远端最新 tag ({latestRemoteTagStr})，需要更新！");

        Assert.True(
            localVersion == remoteVersion,
            $"本地版本 ({localVersionStr}) 与远端最新 tag ({latestRemoteTagStr}) 不同。" +
            "如果代码有变更但版本未更新，请创建新的 tag 并更新版本号。");
    }
}
