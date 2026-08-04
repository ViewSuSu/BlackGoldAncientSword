using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BlackGoldAncientSword.Downloader.Infrastructure;
using BlackGoldAncientSword.Downloader.Shell;
using BlackGoldAncientSword.Downloader.ViewModels;

namespace BlackGoldAncientSword.Downloader.Services
{
    /// <summary>
    /// 主流程：
    /// 1. 查询 Gitee latest release，解析分卷安装包资产列表
    /// 2. HEAD 预估总大小
    /// 3. 顺序流式下载到系统 %TEMP% 下的临时目录（进度 0..100 + 4-stat 实时刷新）
    /// 4. 打开主安装 exe（UseShellExecute=true 触发 UAC），不等待其结束
    /// 5. 关闭下载器窗口并退出（临时文件由 Inno Setup 使用，Setup 结束后系统清理 %TEMP% 时回收）
    /// 取消 / 异常路径：清理临时目录后退出
    /// 进程崩溃 / 用户从任务管理器 kill：由 App.OnExit / ProcessExit 兜底清理（仅下载阶段有效）
    /// </summary>
    public sealed class DownloaderRunner
    {
        private readonly DownloadViewModel _vm;
        private readonly DownloadWindow _window;
        private readonly Dispatcher _ui;
        private CancellationTokenSource _cts = new();
        private string? _tempRoot;
        private bool _installerLaunched;

        public DownloaderRunner(DownloadViewModel vm, DownloadWindow window)
        {
            _vm = vm;
            _window = window;
            _ui = window.Dispatcher;
            _window.CancelRequested += (_, _) => _cts.Cancel();
            _window.RetryRequested += (_, _) => Restart();
            _window.Closed += (_, _) => _cts.Cancel();
            _window.IsCancellable = true;

            // 进程级兜底清理：任务管理器 kill、系统关机、未处理异常都能触发。
            // 只有在 Runner 明确"启动了安装程序"后（_installerLaunched=true）才跳过删除，
            // 因为 Inno Setup 还在读取临时目录里的 .bin 分卷。
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { TryCleanupTempSafely(); ProcLog.Flush(); };
            Application.Current.SessionEnding += (_, _) => { TryCleanupTempSafely(); ProcLog.Flush(); };
        }

        // ============ 主入口 ============

        public async Task RunAsync()
        {
            try
            {
                await StartFlowAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ProcLog.Warning(nameof(DownloaderRunner), "download cancelled by user");
                CleanupAndExit(exitCode: 2);
            }
            catch (Exception ex)
            {
                ProcLog.Error(ex, nameof(DownloaderRunner), "download flow failed");
                Debug.WriteLine($"[Downloader] 主流程失败: {ex}");
                _ui.Invoke(() => ShowError(ex.Message));
                // 错误态：清临时文件、保留窗口等用户点"重试"或关窗
                TryCleanupTempSafely();
            }
        }

        private async Task StartFlowAsync(CancellationToken ct)
        {
            _installerLaunched = false;

            // === Step 1. 查询 latest release ===
            _ui.Invoke(() =>
            {
                _vm.IsError = false;
                _vm.ErrorMessage = string.Empty;
                _vm.IsIndeterminate = true;
                _vm.IsBusy = true;
                _vm.PhaseText = "正在查询最新版本";
                _vm.BottomHintText = "连接 Gitee 服务器中...";
                _vm.Percent = 0;
                _vm.CurrentFileName = string.Empty;
                _vm.CurrentFilePercent = 0;
                _vm.FileIndex = 0;
                _vm.FileTotal = 0;
                _vm.SpeedText = "—";
                _vm.DownloadedText = "—";
                _vm.TotalSizeText = "—";
                _vm.EtaText = "—";
            });

            var fetcher = new GiteeAssetsFetcher();
            var resolved = await fetcher.FetchAsync(ct).ConfigureAwait(false);

            _ui.Invoke(() =>
            {
                _vm.VersionTag = resolved.TagName;
                _vm.IsIndeterminate = false;
            });

            // === Step 2. 建临时目录 ===
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "BlackGoldAncientSword-Downloader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            // === Step 3. 下载全部资产 ===
            var installerPath = await DownloadAllAsync(resolved, ct).ConfigureAwait(false);

            // === Step 4. 打开安装程序，不等待 ===
            _ui.Invoke(() =>
            {
                _vm.IsBusy = false;
                _vm.PhaseText = "下载完成";
                _vm.Percent = 100;
                _vm.SpeedText = "—";
                _vm.EtaText = "—";
                _vm.CurrentFilePercent = 100;
                _vm.BottomHintText = "正在打开安装程序...";
            });

            var launcherProc = LaunchInstaller(installerPath);

            // === Step 5. 把安装向导拉到最前 ===
            // 关键：Inno Setup 通常 requireAdministrator，UAC 后由 Windows 派生新的提权进程；
            // launcherProc 会立即退出，MainWindowHandle 拿不到。Foregrounder 会枚举顶层窗口
            // 按 TWizardForm 类名 / 产品名标题命中真正的向导窗口并强制前置。
            _ui.Invoke(() =>
            {
                _vm.BottomHintText = "正在启动安装向导，请注意屏幕上弹出的安装窗口...";
                _window.IsCancellable = false;
            });

            try
            {
                await InstallerForegrounder.BringInstallerToFrontAsync(
                    launcherProc, "BlackGoldAncientSword").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloader] 前置安装向导失败: {ex.Message}");
            }
            finally
            {
                try { launcherProc?.Dispose(); } catch { }
            }

            // 收尾：关窗退出。临时文件交给 Inno Setup 使用，Setup 结束后系统 %TEMP% 清理机制会回收。
            _ui.Invoke(() => _vm.BottomHintText = "安装向导已在前台打开，下载器即将关闭");
            await Task.Delay(600, CancellationToken.None).ConfigureAwait(false);
            CleanupAndExit(exitCode: 0, keepTempFiles: true);
        }

        // ============ 下载 ============

        /// <summary>
        /// 顺序下载全部资产。返回主安装 exe 的本地完整路径。
        /// 进度条 0..100 全部分配给下载（无解压阶段）。
        /// </summary>
        private async Task<string> DownloadAllAsync(ResolvedInstaller resolved, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword.Downloader");

            var assets = new List<GiteeAssetInfo> { resolved.Installer };
            foreach (var v in resolved.DataVolumes) assets.Add(v);
            int total = assets.Count;

            _ui.Invoke(() =>
            {
                _vm.PhaseText = "正在获取分卷信息";
                _vm.BottomHintText = "预估分卷大小中...";
                _vm.FileTotal = total;
                _vm.FileIndex = 0;
                _vm.IsBusy = true;
            });

            // HEAD 预估总大小；Gitee API 不返回 asset size，只能自己探。
            // 单个失败静默：SizeText 会退化为只显示已下载字节数。
            long estimatedTotal = 0;
            var sizes = new long[total];
            for (int i = 0; i < assets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var hresp = await http.SendAsync(
                        new HttpRequestMessage(HttpMethod.Head, assets[i].Url), ct).ConfigureAwait(false);
                    var cl = hresp.Content.Headers.ContentLength;
                    if (cl.HasValue && cl.Value > 0)
                    {
                        sizes[i] = cl.Value;
                        estimatedTotal += cl.Value;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* HEAD 失败忽略 */ }
            }

            string totalSizeText = estimatedTotal > 0 ? FormatBytes(estimatedTotal) : "—";
            _ui.Invoke(() =>
            {
                _vm.PhaseText = "正在下载";
                _vm.BottomHintText = "下载完成后将打开安装程序，由您自行执行安装";
                _vm.TotalSizeText = totalSizeText;
                _vm.DownloadedText = "0 B";
                _vm.Percent = 0;
            });

            long totalDownloaded = 0;
            string? installerLocalPath = null;

            for (int idx = 0; idx < assets.Count; idx++)
            {
                ct.ThrowIfCancellationRequested();
                var asset = assets[idx];
                var localPath = Path.Combine(_tempRoot!, asset.Name);
                if (idx == 0) installerLocalPath = localPath;

                int _idx = idx + 1;
                string _name = asset.Name;
                long _fileTotalSize = sizes[idx];

                _ui.Invoke(() =>
                {
                    _vm.FileIndex = _idx;
                    _vm.CurrentFileName = _name;
                    _vm.CurrentFilePercent = 0;
                });

                using var resp = await http.GetAsync(
                    asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                // Content-Length 若前面 HEAD 失败，这里再取一次
                if (_fileTotalSize <= 0 && resp.Content.Headers.ContentLength.HasValue)
                    _fileTotalSize = resp.Content.Headers.ContentLength.Value;

                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(
                    localPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                int read;
                var lastReport = DateTime.UtcNow;
                long lastCopied = totalDownloaded;
                long fileCopied = 0;
                double currentSpeed = 0;

                while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    totalDownloaded += read;
                    fileCopied += read;

                    var now = DateTime.UtcNow;
                    var dt = (now - lastReport).TotalSeconds;
                    if (dt >= 0.25)
                    {
                        var inst = (totalDownloaded - lastCopied) / dt;
                        currentSpeed = currentSpeed == 0 ? inst : currentSpeed * 0.6 + inst * 0.4;
                        lastReport = now;
                        lastCopied = totalDownloaded;

                        double overallPct = estimatedTotal > 0
                            ? Math.Min(100.0, totalDownloaded * 100.0 / estimatedTotal)
                            : 0;
                        double filePct = _fileTotalSize > 0
                            ? Math.Min(100.0, fileCopied * 100.0 / _fileTotalSize)
                            : 0;

                        long _dl = totalDownloaded;
                        double _spd = currentSpeed;
                        long _est = estimatedTotal;
                        string _dlText = FormatBytes(_dl);
                        string _speedText = $"{FormatBytes((long)_spd)}/s";
                        string _etaText = (_spd > 1 && _est > _dl)
                            ? FormatEta((_est - _dl) / _spd)
                            : "计算中";

                        _ui.Invoke(() =>
                        {
                            _vm.Percent = overallPct;
                            _vm.CurrentFilePercent = filePct;
                            _vm.DownloadedText = _dlText;
                            _vm.SpeedText = _speedText;
                            _vm.EtaText = _etaText;
                        });
                    }
                }

                await dst.FlushAsync(ct).ConfigureAwait(false);

                _ui.Invoke(() => _vm.CurrentFilePercent = 100);
            }

            string finalDlText = FormatBytes(totalDownloaded);
            _ui.Invoke(() =>
            {
                _vm.Percent = 100;
                _vm.DownloadedText = finalDlText;
                if (estimatedTotal <= 0) _vm.TotalSizeText = finalDlText;
                _vm.EtaText = "0 秒";
            });

            return installerLocalPath!;
        }

        // ============ 打开安装程序（不等待） ============

        /// <summary>启动 Inno Setup 主 exe。返回 launcher 进程句柄（caller 负责 Dispose）。</summary>
        private Process LaunchInstaller(string installerPath)
        {
            if (!File.Exists(installerPath))
                throw new FileNotFoundException("下载完成后未找到主安装程序", installerPath);

            // 关键：Process.Start 之前授权任意进程抢占前台，让 UAC 后的 Setup 进程能拿到焦点，
            // 否则 Windows 会因焦点竞态把 Setup 窗口降级为任务栏闪烁而不置前。
            InstallerForegrounder.PrepareForegroundHandover();

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = _tempRoot,  // Inno DiskSpanning 依赖同目录 .bin 分卷
                UseShellExecute = true,         // 走 shell 语义，触发 UAC 提权
            };

            var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("无法启动安装程序，可能被杀软拦截或用户取消 UAC");

            _installerLaunched = true;
            ProcLog.Info(nameof(DownloaderRunner), $"installer launched, PID={proc.Id}");
            Debug.WriteLine($"[Downloader] 安装程序已启动，PID={proc.Id}");
            return proc;
        }

        // ============ 错误态 ============

        private void ShowError(string message)
        {
            _vm.IsError = true;
            _vm.ErrorMessage = message;
            _vm.IsBusy = false;
            _vm.IsIndeterminate = false;
            _vm.PhaseText = "已停止";
            _vm.BottomHintText = "点击\"重试\"重新开始，或关闭窗口退出";
        }

        // ============ 重试 ============

        /// <summary>由用户在错误态点\"重试\"触发。</summary>
        private void Restart()
        {
            if (!_vm.IsError) return;
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            // 清掉上次残留的临时目录（如果有）
            TryCleanupTempSafely();
            _tempRoot = null;

            _ = RunAsync();
        }

        // ============ 清理临时目录 ============

        /// <summary>
        /// 尝试删除临时目录。安全 no-throw，供正常 CleanupAndExit / ProcessExit / SessionEnding 兜底调用。
        /// 若安装程序已启动（_installerLaunched==true），跳过删除避免破坏正在进行的 Inno Setup 安装。
        /// </summary>
        private void TryCleanupTempSafely()
        {
            if (_installerLaunched) return; // Setup 还在读 .bin，不能删
            try
            {
                if (_tempRoot != null && Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                    Debug.WriteLine($"[Downloader] 已清理临时目录: {_tempRoot}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Downloader] 清理临时目录失败: {ex.Message}");
            }
        }

        // ============ 收尾退出 ============

        /// <param name="keepTempFiles">
        /// 成功启动安装程序后必须传 true：Inno Setup 还在读 .bin 分卷，
        /// 若立即删除会导致安装中断。
        /// </param>
        private void CleanupAndExit(int exitCode, bool keepTempFiles = false)
        {
            if (!keepTempFiles) TryCleanupTempSafely();

            _ui.BeginInvoke(() =>
            {
                _window.IsCancellable = false;
                _window.ForceClose();
                // 硬退出前刷新日志队列，否则 Async sink 里未落盘的日志会丢。
                ProcLog.Flush();
                Environment.Exit(exitCode);
            });
        }

        // ============ Utilities ============

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.0} MB";
            double gb = mb / 1024.0;
            return $"{gb:0.00} GB";
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "计算中";
            if (seconds < 60) return $"{seconds:0} 秒";
            if (seconds < 3600) return $"{seconds / 60:0} 分 {seconds % 60:0} 秒";
            return $"{seconds / 3600:0} 时 {(seconds % 3600) / 60:0} 分";
        }
    }
}
