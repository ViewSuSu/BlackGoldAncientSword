using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BlackGoldAncientSword.Downloader.Services
{
    /// <summary>Gitee release asset 元信息。</summary>
    public sealed class GiteeAssetInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
    }

    /// <summary>
    /// 已解析的一次 latest release 结果：主安装 exe + 全部数据分卷 bin。
    /// 命名规则来源 setup.iss：OutputBaseFilename=BlackGoldAncientSword-{ver}-win-x64-Setup-Split
    ///   - 主 exe：BlackGoldAncientSword-{ver}-win-x64-Setup-Split.exe
    ///   - 数据分卷（Inno DiskSpanning 默认）：BlackGoldAncientSword-{ver}-win-x64-Setup-Split-1.bin ...
    /// </summary>
    public sealed class ResolvedInstaller
    {
        public string TagName { get; init; } = string.Empty;
        public GiteeAssetInfo Installer { get; init; } = default!;
        public IReadOnlyList<GiteeAssetInfo> DataVolumes { get; init; } = Array.Empty<GiteeAssetInfo>();

        /// <summary>下载顺序：主 exe 先下（体积小），bin 分卷后下。</summary>
        public IEnumerable<GiteeAssetInfo> AllInDownloadOrder()
        {
            yield return Installer;
            foreach (var v in DataVolumes) yield return v;
        }
    }

    /// <summary>
    /// 独立实现，不引用 Framework 项目里的 GiteeReleaseService / UpdateService。
    ///
    /// 设计要点：不调 Gitee REST API（/api/v5/repos/.../releases/latest），
    /// 因为未鉴权请求受 Gitee IP 级 rate limit 保护，NAT / CGN / 云出口下极易命中
    /// "403 Forbidden (Rate Limit Exceeded)"，令下载器首步就崩。
    ///
    /// 改用 CDN 直链模式：
    ///   1. 版本号来自 Assembly.GetExecutingAssembly().GetName().Version，由 CI
    ///      dotnet publish /p:Version={new_version} 在发版时注入（csproj 默认 1.0.0.0
    ///      仅本地调试兜底，若 release 缺该版本 asset 会在主 exe HEAD 时报错）。
    ///   2. tag 名 = "v" + 版本号，asset 名遵循 setup.iss 的 OutputBaseFilename 约定，
    ///      拼出主 exe 直链后 HEAD 探测存在性；不存在直接抛，界面弹重试。
    ///   3. .bin 分卷数量未知（Inno DiskSpanning 按盘 size 决定分几卷），从 -1.bin 起
    ///      逐个 HEAD 探测，碰到 404 停。HEAD 走 CDN foruda.gitee.com，不走 API，不受
    ///      rate limit 约束。
    /// </summary>
    public sealed class GiteeAssetsFetcher
    {
        private const string ReleaseDownloadBase =
            "https://gitee.com/SususuChang/BlackGoldAncientSword/releases/download";

        /// <summary>
        /// 分卷探测上限。Inno DiskSpanning 默认单卷 100MB 上下，安装包 500MB 时约 5 卷，
        /// 上限设 50 已远超实际可能值，避免恶意 CDN / 网络异常时无限探测。
        /// </summary>
        private const int MaxVolumeProbe = 50;

        /// <summary>
        /// asset 名称模板。占位符 {0} = 版本号（不含 v 前缀），例如 1.0.0.1。
        /// 与 setup.iss OutputBaseFilename 约定完全对齐。
        /// </summary>
        private const string InstallerNameFormat = "BlackGoldAncientSword-{0}-win-x64-Setup-Split.exe";
        private const string VolumeNameFormat = "BlackGoldAncientSword-{0}-win-x64-Setup-Split-{1}.bin";

        /// <summary>拼出主 exe + 全部数据分卷直链，HEAD 探测存在性。找不到主 exe 时抛。</summary>
        public async Task<ResolvedInstaller> FetchAsync(CancellationToken ct)
        {
            var version = ResolveVersion();
            var tagName = "v" + version;

            var installerName = string.Format(InstallerNameFormat, version);
            var installerUrl = $"{ReleaseDownloadBase}/{tagName}/{installerName}";

            // HEAD 请求可能被 302 到 foruda.gitee.com CDN，AllowAutoRedirect 默认 true，
            // 单次 HEAD 15 秒超时兜底 CDN 偶发慢响应。
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword.Downloader");

            if (!await AssetExistsAsync(http, installerUrl, ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"在 Gitee release 中未找到主安装程序：{installerName}\n" +
                    $"URL：{installerUrl}\n" +
                    $"可能原因：当前 Downloader 版本（{version}）与 release 版本不一致，" +
                    "请到 https://gitee.com/SususuChang/BlackGoldAncientSword/releases 下载最新版下载器。");
            }

            var volumes = new List<GiteeAssetInfo>();
            for (int i = 1; i <= MaxVolumeProbe; i++)
            {
                ct.ThrowIfCancellationRequested();

                var name = string.Format(VolumeNameFormat, version, i);
                var url = $"{ReleaseDownloadBase}/{tagName}/{name}";

                if (!await AssetExistsAsync(http, url, ct).ConfigureAwait(false))
                    break;

                volumes.Add(new GiteeAssetInfo { Name = name, Url = url });
            }

            return new ResolvedInstaller
            {
                TagName = tagName,
                Installer = new GiteeAssetInfo { Name = installerName, Url = installerUrl },
                DataVolumes = volumes,
            };
        }

        /// <summary>
        /// 从当前 Downloader 程序集读取 CI 注入的 Version。
        /// 本地调试未传 /p:Version 时会拿到 csproj 默认的 1.0.0.0，FetchAsync 会在 HEAD 主 exe 时报错，
        /// 由界面提示用户去 release 页手动下载最新版下载器。
        /// </summary>
        private static string ResolveVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null) return "0.0.0.0";

            // asset 名称是 4 段版本号，Version.ToString() 默认输出全部有效段（Revision 有值时输出 4 段）。
            // 显式指定 fieldCount=4 避免 Revision=0 时被裁成 3 段（1.0.0.0 → 1.0.0）。
            return v.ToString(4);
        }

        private static async Task<bool> AssetExistsAsync(HttpClient http, string url, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                return resp.StatusCode == HttpStatusCode.OK;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 网络异常按不存在处理，让上层报"未找到"，避免死循环探测
                return false;
            }
        }
    }
}
