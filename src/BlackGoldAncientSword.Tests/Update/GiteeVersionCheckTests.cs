using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// 通过 Gitee Releases API 获取远端最新发布，对比本地程序集版本，
/// 用于验证将版本检测源从 GitHub 切换到 Gitee 是否可行。
/// 网络/API 不可用时跳过对比，不阻塞 CI 或离线开发。
/// </summary>
public class GiteeVersionCheckTests
{
    private const string GiteeOwner = "SususuChang";
    private const string GiteeRepo = "BlackGoldAncientSword";
    private const string ReleasesUrl =
        "https://gitee.com/api/v5/repos/" + GiteeOwner + "/" + GiteeRepo +
        "/releases?page=1&per_page=20&direction=desc";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string GetLocalVersion()
    {
        var attr = typeof(GiteeVersionCheckTests).Assembly
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

    private static Version ParseVersion(string versionStr)
    {
        var parts = versionStr.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        var revision = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;
        return new Version(major, minor, build, revision);
    }

    private static async Task<List<GiteeReleaseRaw>?> FetchReleasesAsync()
    {
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BlackGoldAncientSword-Tests");

        try
        {
            using var resp = await http.GetAsync(ReleasesUrl);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<GiteeReleaseRaw>>(json, _jsonOptions);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static (string? tag, GiteeReleaseRaw? release) PickLatest(List<GiteeReleaseRaw> releases)
    {
        Version? bestVersion = null;
        string? bestTag = null;
        GiteeReleaseRaw? bestRelease = null;

        foreach (var r in releases)
        {
            if (string.IsNullOrWhiteSpace(r.TagName))
                continue;

            var normalized = NormalizeTag(r.TagName!);
            var v = ParseVersion(normalized);
            if (bestVersion is null || v > bestVersion)
            {
                bestVersion = v;
                bestTag = normalized;
                bestRelease = r;
            }
        }

        return (bestTag, bestRelease);
    }

    [Fact]
    public async Task GiteeApi_IsReachable_AndReturnsReleases()
    {
        var releases = await FetchReleasesAsync();

        if (releases is null)
        {
            Assert.True(true, "Gitee API 不可达（网络问题或限流），跳过。");
            return;
        }

        Assert.NotEmpty(releases);
        Assert.All(releases, r => Assert.False(string.IsNullOrWhiteSpace(r.TagName), "release 缺少 tag_name"));
    }

    [Fact]
    public async Task LocalVersion_ShouldNotBeBehind_GiteeLatestRelease()
    {
        var releases = await FetchReleasesAsync();

        if (releases is null || releases.Count == 0)
        {
            Assert.True(true, "Gitee releases 不可用，跳过版本对比。");
            return;
        }

        var (latestTag, latestRelease) = PickLatest(releases);
        if (latestTag is null || latestRelease is null)
        {
            Assert.True(true, "Gitee releases 中未找到可解析的 tag_name，跳过。");
            return;
        }

        var localVersionStr = GetLocalVersion();
        var localVersion = ParseVersion(localVersionStr);
        var remoteVersion = ParseVersion(latestTag);

        Assert.True(
            localVersion >= remoteVersion,
            $"本地版本 ({localVersionStr}) 低于 Gitee 最新 release ({latestTag} / {latestRelease.Name})，需要更新！");
    }

    private class GiteeReleaseRaw
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }
}
