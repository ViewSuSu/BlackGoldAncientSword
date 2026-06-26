using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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
        private readonly Dispatcher _ui;
        private readonly CancellationTokenSource _cts = new();
        private string? _tempRoot;

        public UpdaterRunner(UpdateOptions options, UpdateViewModel vm, UpdateWindow window)
        {
            _options = options;
            _vm = vm;
            _window = window;
            _ui = window.Dispatcher;
            _window.CancelRequested += (_, _) => _cts.Cancel();
            _window.Closed += (_, _) => _cts.Cancel();
            _window.IsCancellable = true;
        }

        public async Task RunAsync()
        {
            try
            {
                var zipPath = await DownloadAsync(_cts.Token).ConfigureAwait(false);
                var extractDir = await ExtractAsync(zipPath, _cts.Token).ConfigureAwait(false);
                await EnsureMainAppClosedAsync().ConfigureAwait(false);
                // 进入文件覆盖阶段：禁止用户中途取消（半覆盖无法回滚）
                _ui.Invoke(() => _window.IsCancellable = false);
                await CopyOverAsync(extractDir).ConfigureAwait(false);
                LaunchMainApp();
                CleanupAndExit(0);
            }
            catch (OperationCanceledException)
            {
                CleanupAndExit(2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Updater] 更新失败: {ex}");
                _ui.Invoke(() => MessageBox.Show(
                    _window,
                    $"更新失败：{ex.Message}",
                    "BlackGoldAncientSword 在线更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
                CleanupAndExit(1);
            }
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

            _ui.Invoke(() => _vm.StatusText = "正在连接下载服务器...");

            using var resp = await http.GetAsync(
                _options.ZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long? totalBytes = resp.Content.Headers.ContentLength;
            _ui.Invoke(() =>
            {
                _vm.StatusText = "正在下载更新包...";
                _vm.SizeText = totalBytes.HasValue ? $"0 / {FormatBytes(totalBytes.Value)}" : "0";
            });

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(
                zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long copied = 0;
            var startedAt = DateTime.UtcNow;
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

                    _ui.Invoke(() =>
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
                    });
                }
            }

            await dst.FlushAsync(ct).ConfigureAwait(false);
            _ui.Invoke(() =>
            {
                _vm.Percent = DownloadCap;
                _vm.EtaText = string.Empty;
                _vm.StatusText = "下载完成，正在解压...";
            });
            return zipPath;
        }

        // ============ 2. 解压 ============

        private async Task<string> ExtractAsync(string zipPath, CancellationToken ct)
        {
            var extractDir = Path.Combine(_tempRoot!, "extracted");
            Directory.CreateDirectory(extractDir);

            // 切到解压阶段：保留下载阶段的速度 / 大小 / ETA 显示，只换状态行
            _ui.Invoke(() => _vm.StatusText = "正在解压更新包...");

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
                    // 避免每条 entry 都 Invoke 把 UI 线程打爆
                    var nowTick = Environment.TickCount64;
                    if (done == total || nowTick - lastUiTick >= 80)
                    {
                        lastUiTick = nowTick;
                        double frac = total > 0 ? (double)done / total : 1;
                        double p = DownloadCap + (ExtractCap - DownloadCap) * frac;
                        int _done = done;
                        int _total = total;
                        _ui.Invoke(() =>
                        {
                            _vm.Percent = p;
                            _vm.StatusText = $"正在解压更新包... {_done} / {_total}";
                        });
                    }
                }
            }, ct).ConfigureAwait(false);

            _ui.Invoke(() => _vm.Percent = ExtractCap);
            return extractDir;
        }

        // ============ 3. 检测并关闭主程序 ============

        private async Task EnsureMainAppClosedAsync()
        {
            while (true)
            {
                var running = GetMainProcesses();
                if (running.Count == 0) return;

                var tcs = new TaskCompletionSource<bool>();
                _ui.Invoke(() =>
                {
                    _vm.StatusText = $"检测到主程序 {_options.MainExeName} 正在运行，需要关闭后继续。";
                    var result = MessageBox.Show(
                        _window,
                        $"检测到 {_options.MainExeName} 正在运行，必须先关闭才能完成更新。\n\n点击 \"强制关闭\" 立即结束主程序，点击 \"取消\" 退出更新。",
                        "需要关闭主程序",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    tcs.SetResult(result == MessageBoxResult.OK);
                });

                bool forceClose = await tcs.Task.ConfigureAwait(false);
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
                    // 32/64 位 / 权限不足时拿不到 MainModule，保守也加入
                }
                list.Add(p);
            }
            return list;
        }

        // ============ 4. 覆盖到目标目录 ============

        private async Task CopyOverAsync(string extractDir)
        {
            _ui.Invoke(() => _vm.StatusText = "正在安装文件，请稍候...");

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
                        // 锁定 / 权限 / 路径错误：全部静默跳过（按用户要求不提示）
                        Debug.WriteLine($"[Updater] 跳过 {dst}: {ex.Message}");
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
                        _ui.Invoke(() =>
                        {
                            _vm.Percent = Math.Min(100, p);
                            _vm.StatusText = $"正在安装文件... {_done} / {_total}";
                        });
                    }
                }
            }).ConfigureAwait(false);

            _ui.Invoke(() =>
            {
                _vm.Percent = 100;
                _vm.StatusText = "更新完成，正在重启主程序...";
            });
        }

        // ============ 5. 重启 + 退出 ============

        private void LaunchMainApp()
        {
            if (!_options.RestartAfterInstall) return;
            try
            {
                var exe = _options.MainExeFullPath;
                if (!File.Exists(exe))
                {
                    Debug.WriteLine($"[Updater] 主程序不存在，跳过启动: {exe}");
                    return;
                }
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
            _ui.BeginInvoke(() =>
            {
                _window.IsCancellable = false;
                _window.ForceClose();
                Environment.Exit(code);
            });
        }

        // ============ 工具 ============

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
