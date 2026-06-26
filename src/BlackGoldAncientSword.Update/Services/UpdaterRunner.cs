using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BlackGoldAncientSword.Update.Shell;
using BlackGoldAncientSword.Update.ViewModels;

namespace BlackGoldAncientSword.Update.Services
{
    /// <summary>
    /// 更新流程：
    /// 1. 下载远端 zip（进度占 0..90）
    /// 2. 解压到临时目录（进度推到 ~95）
    /// 3. 检测主程序是否运行；若运行 → 弹强制关闭对话框，用户点了才继续
    /// 4. 把解压后的文件全量覆盖到目标目录（被占用 / 异常一律静默跳过）
    /// 5. 重启主程序，退出 updater
    /// </summary>
    public sealed class UpdaterRunner
    {
        private const double DownloadCap = 90.0;
        private const double ExtractCap = 98.0;

        private readonly UpdateOptions _options;
        private readonly UpdateViewModel _vm;
        private readonly UpdateWindow _window;
        private readonly CancellationTokenSource _cts = new();
        private string? _tempRoot;

        // 文件覆盖阶段失败的相对路径 + 原因（汇总在结束时弹窗一次性告知，避免每个文件弹）
        private readonly List<string> _copySkipped = new();

        // 重启主程序失败 / 主程序不存在 时记录的提示，结束时一并弹窗
        private string? _postRunWarning;

        public UpdaterRunner(UpdateOptions options, UpdateViewModel vm, UpdateWindow window)
        {
            _options = options;
            _vm = vm;
            _window = window;
            _window.CancelRequested += (_, _) => _cts.Cancel();
            _window.Closed += (_, _) => _cts.Cancel();
            _window.IsCancellable = true;
        }

        public async Task RunAsync()
        {
            try
            {
                ValidateOptions();
                var zipPath = await DownloadAsync(_cts.Token).ConfigureAwait(false);
                var extractDir = await ExtractAsync(zipPath, _cts.Token).ConfigureAwait(false);
                await EnsureMainAppClosedAsync().ConfigureAwait(false);
                // 进入文件覆盖阶段：禁止用户中途取消（半覆盖无法回滚）
                await OnUiAsync(() => _window.IsCancellable = false).ConfigureAwait(false);
                await CopyOverAsync(extractDir).ConfigureAwait(false);
                LaunchMainApp();
                await ShowPostRunWarningsAsync().ConfigureAwait(false);
                CleanupAndExit(0);
            }
            catch (OperationCanceledException)
            {
                CleanupAndExit(2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] 更新失败: {ex}");
                await OnUiAsync(async () =>
                {
                    await ConfirmDialog.ShowErrorAsync(
                        _window,
                        "BlackGoldAncientSword 在线更新",
                        $"更新失败：{ex.Message}").ConfigureAwait(false);
                }).ConfigureAwait(false);
                CleanupAndExit(1);
            }
        }

        // ============ 0. 前置参数校验 ============

        /// <summary>
        /// 所有"不符合 update 需要的条件"在这里集中校验，
        /// 任意不通过都抛 InvalidOperationException，让 RunAsync 外层 catch 统一弹窗。
        /// </summary>
        private void ValidateOptions()
        {
            if (string.IsNullOrWhiteSpace(_options.ZipUrl))
                throw new InvalidOperationException("缺少必需参数 --url <zipUrl>。");

            if (!_options.ZipUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !_options.ZipUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"--url 必须是 http(s) 直链，当前值：{_options.ZipUrl}");

            if (string.IsNullOrWhiteSpace(_options.TargetDirectory))
                throw new InvalidOperationException("目标安装目录为空。");

            if (!Directory.Exists(_options.TargetDirectory))
                throw new InvalidOperationException(
                    $"目标安装目录不存在：{_options.TargetDirectory}");

            if (string.IsNullOrWhiteSpace(_options.MainExeName))
                throw new InvalidOperationException("主程序 exe 名为空（--main-exe）。");
        }

        /// <summary>
        /// 文件覆盖阶段静默跳过 / 重启主程序失败 这类"已完成但有问题"的情况，
        /// 在退出前一次性汇总弹窗告诉用户，不要静默吞掉。
        /// </summary>
        private async Task ShowPostRunWarningsAsync()
        {
            if (_copySkipped.Count == 0 && _postRunWarning == null) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("更新已完成，但有以下情况需要您留意：\n");

            if (_copySkipped.Count > 0)
            {
                sb.Append($"\n● {_copySkipped.Count} 个文件未能写入（可能被占用或权限不足）：\n");
                int show = Math.Min(_copySkipped.Count, 10);
                for (int i = 0; i < show; i++)
                {
                    sb.Append("  - ").Append(_copySkipped[i]).Append('\n');
                }
                if (_copySkipped.Count > show)
                    sb.Append($"  ...（其余 {_copySkipped.Count - show} 条省略）\n");
            }

            if (_postRunWarning != null)
            {
                sb.Append("\n● ").Append(_postRunWarning).Append('\n');
            }

            await OnUiAsync(async () =>
            {
                await ConfirmDialog.ShowErrorAsync(
                    _window,
                    "BlackGoldAncientSword 在线更新",
                    sb.ToString()).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // ============ 1. 下载 ============

        private async Task<string> DownloadAsync(CancellationToken ct)
        {
            // 临时目录放在目标安装目录下（不是系统 %TEMP%），方便排查也避免跨盘复制
            _tempRoot = Path.Combine(
                _options.TargetDirectory,
                ".update_temp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            var zipPath = Path.Combine(_tempRoot, "update.zip");

            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword.Update");

            await OnUiAsync(() => _vm.StatusText = "正在连接下载服务器...").ConfigureAwait(false);

            using var resp = await http.GetAsync(
                _options.ZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long? totalBytes = resp.Content.Headers.ContentLength;
            await OnUiAsync(() =>
            {
                _vm.StatusText = "正在下载更新包...";
                _vm.SizeText = totalBytes.HasValue ? $"0 / {FormatBytes(totalBytes.Value)}" : "0";
            }).ConfigureAwait(false);

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(
                zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long copied = 0;
            var lastReport = DateTime.UtcNow;
            long lastCopied = 0;
            double currentSpeed = 0; // bytes/sec
            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                copied += read;

                var now = DateTime.UtcNow;
                var dt = (now - lastReport).TotalSeconds;
                if (dt >= 0.25)
                {
                    var inst = (copied - lastCopied) / dt;
                    // 指数平滑速度，避免抖动
                    currentSpeed = currentSpeed == 0 ? inst : currentSpeed * 0.6 + inst * 0.4;
                    lastReport = now;
                    lastCopied = copied;

                    long _copied = copied;
                    long? _total = totalBytes;
                    double _speed = currentSpeed;

                    await OnUiAsync(() =>
                    {
                        if (_total.HasValue && _total.Value > 0)
                        {
                            _vm.Percent = Math.Min(DownloadCap, _copied * DownloadCap / _total.Value);
                            _vm.SizeText = $"{FormatBytes(_copied)} / {FormatBytes(_total.Value)}";
                            var remain = _total.Value - _copied;
                            _vm.EtaText = _speed > 1 ? $"剩余 {FormatEta(remain / _speed)}" : "剩余 计算中";
                        }
                        else
                        {
                            _vm.SizeText = FormatBytes(_copied);
                            _vm.EtaText = string.Empty;
                        }
                        _vm.SpeedText = $"{FormatBytes((long)_speed)}/s";
                    }).ConfigureAwait(false);
                }
            }

            await dst.FlushAsync(ct).ConfigureAwait(false);
            await OnUiAsync(() =>
            {
                _vm.Percent = DownloadCap;
                _vm.EtaText = string.Empty;
                _vm.StatusText = "下载完成,正在解压...";
            }).ConfigureAwait(false);
            return zipPath;
        }

        // ============ 2. 解压 ============

        private async Task<string> ExtractAsync(string zipPath, CancellationToken ct)
        {
            var extractDir = Path.Combine(_tempRoot!, "extracted");
            Directory.CreateDirectory(extractDir);

            await OnUiAsync(() => _vm.StatusText = "正在解压更新包...").ConfigureAwait(false);

            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                int total = zip.Entries.Count;
                int done = 0;
                long lastUiTick = 0;
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                    // 防 zip slip
                    if (!destPath.StartsWith(extractDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                    done++;

                    // 节流 UI 更新：≥80ms 或最后一个文件才推一次，
                    // 避免每条 entry 都 Post 把 UI 线程打爆
                    var nowTick = Environment.TickCount64;
                    if (done == total || nowTick - lastUiTick >= 80)
                    {
                        lastUiTick = nowTick;
                        double frac = total > 0 ? (double)done / total : 1;
                        double p = DownloadCap + (ExtractCap - DownloadCap) * frac;
                        int _done = done;
                        int _total = total;
                        Dispatcher.UIThread.Post(() =>
                        {
                            _vm.Percent = p;
                            _vm.StatusText = $"正在解压更新包... {_done} / {_total}";
                        });
                    }
                }
            }, ct).ConfigureAwait(false);

            await OnUiAsync(() => _vm.Percent = ExtractCap).ConfigureAwait(false);
            return extractDir;
        }

        // ============ 3. 检测并关闭主程序 ============

        private async Task EnsureMainAppClosedAsync()
        {
            while (true)
            {
                var running = GetMainProcesses();
                if (running.Count == 0) return;

                bool forceClose = false;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    _vm.StatusText = $"检测到主程序 {_options.MainExeName} 正在运行,需要关闭后继续。";
                    forceClose = await ConfirmDialog.ShowConfirmAsync(
                        _window,
                        "需要关闭主程序",
                        $"检测到 {_options.MainExeName} 正在运行,必须先关闭才能完成更新。\n\n点击\"强制关闭\"立即结束主程序,点击\"取消\"退出更新。",
                        okText: "强制关闭",
                        cancelText: "取消").ConfigureAwait(true);
                });

                if (!forceClose) throw new OperationCanceledException();

                foreach (var p in running)
                {
                    try { p.Kill(entireProcessTree: true); }
                    catch (Exception ex) { Debug.WriteLine($"[Updater] kill {p.Id} 失败: {ex.Message}"); }
                    finally { p.Dispose(); }
                }

                // 等待句柄释放
                await Task.Delay(800).ConfigureAwait(false);
            }
        }

        private List<Process> GetMainProcesses()
        {
            var name = _options.MainProcessName;
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path != null
                        && Path.GetFullPath(path).Equals(
                            Path.GetFullPath(_options.MainExeFullPath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(p);
                        continue;
                    }
                }
                catch
                {
                    // 32/64 位 / 权限不足时拿不到 MainModule,保守也加入
                }
                list.Add(p);
            }
            return list;
        }

        // ============ 4. 覆盖到目标目录 ============

        private async Task CopyOverAsync(string extractDir)
        {
            await OnUiAsync(() => _vm.StatusText = "正在安装文件,请稍候...").ConfigureAwait(false);

            string selfExe;
            try { selfExe = Path.GetFullPath(Environment.ProcessPath ?? string.Empty); }
            catch { selfExe = string.Empty; }

            await Task.Run(() =>
            {
                var src = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                int total = src.Length;
                int done = 0;
                long lastUiTick = 0;
                foreach (var srcFile in src)
                {
                    var rel = Path.GetRelativePath(extractDir, srcFile);
                    var dst = Path.Combine(_options.TargetDirectory, rel);

                    // 不覆盖正在运行的 updater 自身
                    try
                    {
                        if (!string.IsNullOrEmpty(selfExe)
                            && string.Equals(Path.GetFullPath(dst), selfExe, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    catch { }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(srcFile, dst, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        // 不可取消阶段不能弹窗中断流程，先累积，最后在 ShowPostRunWarningsAsync 一次性弹
                        Debug.WriteLine($"[Updater] 跳过 {dst}: {ex.Message}");
                        lock (_copySkipped)
                        {
                            _copySkipped.Add($"{rel}: {ex.Message}");
                        }
                    }

                    done++;
                    var nowTick = Environment.TickCount64;
                    if (done == total || nowTick - lastUiTick >= 80)
                    {
                        lastUiTick = nowTick;
                        double frac = total > 0 ? (double)done / total : 1;
                        double p = ExtractCap + (100 - ExtractCap) * frac;
                        int _done = done;
                        int _total = total;
                        Dispatcher.UIThread.Post(() =>
                        {
                            _vm.Percent = Math.Min(100, p);
                            _vm.StatusText = $"正在安装文件... {_done} / {_total}";
                        });
                    }
                }
            }).ConfigureAwait(false);

            await OnUiAsync(() =>
            {
                _vm.Percent = 100;
                _vm.StatusText = "更新完成,正在重启主程序...";
            }).ConfigureAwait(false);
        }

        // ============ 5. 重启 + 退出 ============

        private void LaunchMainApp()
        {
            if (!_options.RestartAfterInstall) return;
            var exe = _options.MainExeFullPath;
            if (!File.Exists(exe))
            {
                Debug.WriteLine($"[Updater] 主程序不存在,跳过启动: {exe}");
                _postRunWarning =
                    $"未找到主程序 {_options.MainExeName}，无法自动重启。\n路径：{exe}\n请手动启动主程序。";
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = _options.TargetDirectory,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] 启动主程序失败: {ex.Message}");
                _postRunWarning =
                    $"自动启动主程序失败：{ex.Message}\n请手动启动 {_options.MainExeName}。";
            }
        }

        private void CleanupAndExit(int code)
        {
            // 用户取消 / 失败 / 成功三条路径都走到这里：
            // 一律删 _tempRoot（已下载 zip + 解压目录），实现"取消即回退"
            try
            {
                if (_tempRoot != null && Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] 清理临时目录失败: {ex.Message}");
            }
            Dispatcher.UIThread.Post(() =>
            {
                _window.IsCancellable = false;
                _window.ForceClose();
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown(code);
                }
                else
                {
                    Environment.Exit(code);
                }
            });
        }

        // ============ 工具 ============

        private static async Task OnUiAsync(Action action)
            => await Dispatcher.UIThread.InvokeAsync(action);

        private static Task OnUiAsync(Func<Task> action)
        {
            var tcs = new TaskCompletionSource();
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

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
