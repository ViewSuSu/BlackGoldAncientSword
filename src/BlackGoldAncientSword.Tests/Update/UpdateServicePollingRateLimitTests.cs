using System.Net;
using System.Net.Http;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Update;

/// <summary>
/// 诊断性集成测试：模拟后台轮询（每 1 分钟一次）对 Gitee `releases/latest` 网页端的真实请求压力，
/// 探测是否会被 Gitee 反爬 / WAF 限流（403 / 429 / 验证码页）。
///
/// 复刻 <see cref="BlackGoldAncientSword.Framework.Services.Implementation.UpdateService.FetchLatestTagAsync"/>
/// 的请求特征：GET releases/latest?_={ts}（cache-buster）+ no-cache/no-store 头 + AllowAutoRedirect=false，
/// 正常应得到 302 → Location 指向 releases/tag/v{version}。
///
/// 默认 Skip：会真实高频打 Gitee，不适合进 CI。手动去掉 Skip 本地跑，从 ITestOutputHelper 看逐次结果。
/// </summary>
public class UpdateServicePollingRateLimitTests
{
    private const string GiteeReleaseLatestUrl =
        "https://gitee.com/SususuChang/BlackGoldAncientSword/releases/latest";

    // 压测轮次：用连续快打放大限流信号，比真等 1min×N 更快暴露阈值。
    private const int BurstCount = 30;

    private readonly ITestOutputHelper _output;

    public UpdateServicePollingRateLimitTests(ITestOutputHelper output) => _output = output;

    private static HttpClient CreatePollingClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword");
        return http;
    }

    private static HttpRequestMessage BuildPollingRequest()
    {
        var url = $"{GiteeReleaseLatestUrl}?_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };
        req.Headers.Pragma.ParseAdd("no-cache");
        return req;
    }

    private static bool IsExpectedRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.Redirect or HttpStatusCode.Found
             or HttpStatusCode.MovedPermanently or HttpStatusCode.SeeOther;

    [Fact(Skip = "诊断用：会真实高频请求 Gitee，手动去掉 Skip 本地运行")]
    public async Task Polling_Burst_ShouldNotBeRateLimited()
    {
        using var http = CreatePollingClient();

        int okRedirect = 0;
        int rateLimited = 0;   // 403 / 429
        int other = 0;
        int failed = 0;        // 网络异常

        for (int i = 1; i <= BurstCount; i++)
        {
            try
            {
                using var req = BuildPollingRequest();
                using var resp = await http.SendAsync(req);
                var code = (int)resp.StatusCode;
                var location = resp.Headers.Location?.ToString() ?? "(无)";

                if (IsExpectedRedirect(resp.StatusCode))
                {
                    okRedirect++;
                    _output.WriteLine($"[{i,2}/{BurstCount}] {code} 302 OK → {location}");
                }
                else if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    rateLimited++;
                    _output.WriteLine($"[{i,2}/{BurstCount}] {code} ⚠ 疑似限流/反爬");
                }
                else
                {
                    other++;
                    _output.WriteLine($"[{i,2}/{BurstCount}] {code} 非预期状态");
                }
            }
            catch (Exception ex)
            {
                failed++;
                _output.WriteLine($"[{i,2}/{BurstCount}] 请求异常: {ex.GetType().Name} {ex.Message}");
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"=== 汇总（连打 {BurstCount} 次）===");
        _output.WriteLine($"302 正常   : {okRedirect}");
        _output.WriteLine($"限流(403/429): {rateLimited}");
        _output.WriteLine($"其它状态   : {other}");
        _output.WriteLine($"网络异常   : {failed}");

        if (okRedirect == 0 && failed == BurstCount)
        {
            Assert.True(true, "Gitee 不可达（全部网络异常），无法判定限流，跳过。");
            return;
        }

        Assert.True(
            rateLimited == 0,
            $"出现 {rateLimited} 次限流/反爬（403/429），轮询存在被 Gitee 拦截风险。");
    }
}
