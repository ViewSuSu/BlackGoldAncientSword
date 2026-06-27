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
// 用 HandyControl 的 MessageBox 替代 System.Windows.MessageBox，外观与主题统一
using HCMessageBox = HandyControl.Controls.MessageBox;

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
            string extractDir;
                if (!string.IsNullOrEmpty(_options.SplitUrl))
                {
                    var combinedPath = await DownloadSplitAsync(_cts.Token).ConfigureAwait(false);
                    extractDir = await ExtractSplitAsync(combinedPath, _cts.Token).ConfigureAwait(false);
                }
                else
                {
                    var zipPath = await DownloadAsync(_cts.Token).ConfigureAwait(false);
                    extractDir = await ExtractAsync(zipPath, _cts.Token).ConfigureAwait(false);
                }
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
                _ui.Invoke(() => HCMessageBox.Show(
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

       // ============ 分卷 zip 下载 ============

        private async Task<string> DownloadSplitAsync(CancellationToken ct)
        {
            _tempRoot = Path.Combine(
                _options.TargetDirectory,
                ".update_temp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            var baseUrl = _options.SplitUrl!;
            if (baseUrl.EndsWith(".001"))
                baseUrl = baseUrl[..^4];

            var combinedPath = Path.Combine(_tempRoot, "update.zip");

            _ui.Invoke(() => _vm.StatusText = "正在连接下载服务器...");

            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlackGoldAncientSword.Update");

            using var combinedStream = new FileStream(
                combinedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            int partNum = 1;
            long totalDownloaded = 0;
            long estimatedTotal = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var partUrl = $"{baseUrl}.{partNum:D3}";

                _ui.Invoke(() => _vm.StatusText = "正在更新...");

                try
                {
                    using var resp = await http.GetAsync(partUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    long partSize = resp.Content.Headers.ContentLength ?? 0;
                    if (partSize > 0)
                    {
                        var newEstimate = partSize * (partNum + 2);
                        if (newEstimate > estimatedTotal)
                            estimatedTotal = newEstimate;
                    }

                    await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[81920];
                    long partCopied = 0;
                    int read;
                    var lastReport = DateTime.UtcNow;
                    long lastCopied = 0;
                    double currentSpeed = 0;

                    while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        await combinedStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        partCopied += read;
                        totalDownloaded += read;

                        var now = DateTime.UtcNow;
                        var dt = (now - lastReport).TotalSeconds;
                        if (dt >= 0.25)
                        {
                            var inst = (totalDownloaded - lastCopied) / dt;
                            currentSpeed = currentSpeed == 0 ? inst : currentSpeed * 0.6 + inst * 0.4;
                            lastReport = now;
                            lastCopied = totalDownloaded;

                            double pct = estimatedTotal > 0
                                ? Math.Min(DownloadCap, totalDownloaded * DownloadCap / estimatedTotal)
                                : 0;

                            long _total = totalDownloaded;
                            double _speed = currentSpeed;
                            long _est = estimatedTotal;

                            _ui.Invoke(() =>
                            {
                                _vm.Percent = pct;
                                _vm.SizeText = _est > 0
                                    ? $"{FormatBytes(_total)} / {FormatBytes(_est)}"
                                    : FormatBytes(_total);
                                _vm.SpeedText = $"{FormatBytes((long)_speed)}/s";
                                if (_speed > 1 && _est > _total)
                                    _vm.EtaText = $"剩余 {FormatEta((_est - _total) / _speed)}";
                                else
                                    _vm.EtaText = "剩余 计算中";
                            });
                        }
                    }

                    partNum++;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // 404 = no more parts, normal end
                    break;
                }
            }

            await combinedStream.FlushAsync(ct).ConfigureAwait(false);

            _ui.Invoke(() =>
            {
                _vm.Percent = DownloadCap;
                _vm.SpeedText = string.Empty;
                _vm.EtaText = string.Empty;
                _vm.StatusText = "下载完成，正在解压...";
            });

            return combinedPath;
        }

        // ============ 2. 分卷 zip 拼接+解压（SharpCompress） ============

        private async Task<string> ExtractSplitAsync(string combinedPath, CancellationToken ct)
        {
            var extractDir = Path.Combine(_tempRoot!, "extracted");
            Directory.CreateDirectory(extractDir);

            _ui.Invoke(() => _vm.StatusText = "正在解压更新包...");

            await Task.Run(() =>
            {
               using var zip = ZipFile.OpenRead(combinedPath);
                var rootPrefix = FindCommonRoot(zip);
               int total = zip.Entries.Count;
               int done = 0;
               long lastUiTick = 0;

               foreach (var entry in zip.Entries)
               {
                   ct.ThrowIfCancellationRequested();
                    var entryName = StripRoot(entry.FullName, rootPrefix);
                    var destPath = Path.GetFullPath(Path.Combine(extractDir, entryName));
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

                    var nowTick = Environment.TickCount64;
                    if (done == total || nowTick - lastUiTick >= 80)
                    {
                        lastUiTick = nowTick;
                        double p = 90 + (8 * (double)done / total);
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

            _ui.Invoke(() => _vm.Percent = 98);
            return extractDir;
        }

        // ============ 3. 单 .zip 解压（System.IO.Compression） ============
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
                var rootPrefix = FindCommonRoot(zip);
               int total = zip.Entries.Count;
               int done = 0;
               long lastUiTick = 0;
               foreach (var entry in zip.Entries)
               {
                   ct.ThrowIfCancellationRequested();
                    var entryName = StripRoot(entry.FullName, rootPrefix);
                    var destPath = Path.GetFullPath(Path.Combine(extractDir, entryName));
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
                    // 注意：MessageBoxButton.OKCancel 在 WPF 中按钮文字固定为系统的"确定/取消"，
                    // 不能自定义为"强制关闭/取消"，因此正文中明确点"确定"=立即结束主程序、
                    // "取消"=退出更新，避免按钮与正文措辞不一致让用户误点。
                    var result = HCMessageBox.Show(
                        $"检测到 {_options.MainExeName} 正在运行，必须先关闭才能完成更新。\n\n点击 \"确定\" 立即结束主程序，点击 \"取消\" 退出更新。",
                        "需要关闭主程序",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    tcs.SetResult(result == MessageBoxResult.OK);
                });

                bool forceClose = await tcs.Task.ConfigureAwait(false);
                if (!forceClose) throw new OperationCanceledException();

                // 记录 kill 失败/未生效的进程，用于循环结束时一次性反馈给用户，
                // 避免按"确定"后悄无声息又反复弹同一个对话框。
                var killFailures = new List<string>();
                foreach (var p in running)
                {
                    int pid = -1;
                    try { pid = p.Id; }
                    catch { }
                    try
                    {
                        // 不用 entireProcessTree:true：Updater 是主程序 spawn 的子进程，
                        // 杀进程树会包含 calling process（Updater 自己），CLR 直接抛
                        // "Cannot be used to terminate a process tree containing the calling process"。
                        // 主程序自己的子进程（如 PaddleOCR-json）已由 JobObject
                        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE 在父进程退出时由 OS 自动清理，
                        // 这里只需 kill 主程序本身即可。
                        p.Kill();
                        // Kill 是异步的，必须等待真正退出，否则下一轮检测仍可能命中。
                        // 超时 3 秒兜底，避免极端情况下被某进程卡死无限阻塞 updater。
                        if (!p.WaitForExit(3000))
                            killFailures.Add($"PID={pid} 在 3 秒内未退出（可能被另一个高权限进程持有）");
                    }
                    catch (Exception ex)
                    {
                        killFailures.Add($"PID={pid} kill 失败：{ex.Message}");
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                // 等待句柄完全释放后再重新枚举进程列表
                await Task.Delay(800).ConfigureAwait(false);

                // 若 800ms 后目标进程仍存活，向用户报错并允许其选择重试或退出，
                // 取代原"静默死循环反复弹同一对话框"的行为。
                var stillAlive = GetMainProcesses();
                if (stillAlive.Count > 0 || killFailures.Count > 0)
                {
                    foreach (var sp in stillAlive) sp.Dispose();
                    var detail = killFailures.Count > 0
                        ? string.Join("\n", killFailures)
                        : $"主程序 {_options.MainExeName} 仍在运行（共 {stillAlive.Count} 个进程）。";

                    var retryTcs = new TaskCompletionSource<bool>();
                    _ui.Invoke(() =>
                    {
                        var r = HCMessageBox.Show(
                            $"无法关闭主程序：\n\n{detail}\n\n常见原因：\n  • Updater 权限低于主程序（主程序以管理员身份运行，需要 Updater 也用管理员身份）\n  • 主程序被守护进程/外部脚本持续拉起\n  • 杀毒软件拦截了 TerminateProcess\n\n点击 \"确定\" 再次尝试关闭，点击 \"取消\" 退出更新。",
                            "关闭主程序失败",
                            MessageBoxButton.OKCancel,
                            MessageBoxImage.Error);
                        retryTcs.SetResult(r == MessageBoxResult.OK);
                    });

                    if (!await retryTcs.Task.ConfigureAwait(false))
                        throw new OperationCanceledException();
                    // 用户选择重试则继续 while(true) 下一轮
                }
            }
        }

        /// <summary>
        /// 找到必须先关闭的主程序进程列表。三段策略，按可靠度递减：
        ///   1. 主程序传了 --main-pid → 直接 GetProcessById(pid)，这是最稳的判定，
        ///      绕开 image name 不一致 / 多会话 / 同名异目录的所有歧义。
        ///   2. PID 命中后，额外按 MainModule 路径扫一遍是否还有"同安装目录的其它实例"
        ///      （用户开了两个相同 App），一并加入待杀列表。
        ///   3. 主程序没传 PID（旧版兼容） → 退回原 image name + MainExeFullPath 路径比对。
        /// </summary>
        private List<Process> GetMainProcesses()
        {
            var targetPath = Path.GetFullPath(_options.MainExeFullPath);
            var seenPids = new HashSet<int>();
            var list = new List<Process>();

            // === 1. PID 优先 ===
            if (_options.MainPid is int pid)
            {
                try
                {
                    // GetProcessById 在进程不存在 / 已退出时抛 ArgumentException，被 catch 吞掉
                    var p = Process.GetProcessById(pid);
                    if (!p.HasExited)
                    {
                        list.Add(p);
                        seenPids.Add(p.Id);
                    }
                    else
                    {
                        p.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Updater] GetProcessById({pid}) 失败（可能进程已退出）: {ex.Message}");
                }
            }

            // === 2 & 3. 路径扫描：捕获同安装目录其它实例 / 兼容未传 PID 的旧主程序 ===
            //
            // 不再只信 Process.GetProcessesByName(imageName) — 因为 dotnet host / publish 选项
            // 不同会导致 image name 是 "dotnet" 等而漏检。直接遍历全部进程按 MainModule 路径命中。
            // 性能上 Process.GetProcesses 数百进程，MainModule 访问几十微秒，整体可接受。
            foreach (var p in Process.GetProcesses())
            {
                if (seenPids.Contains(p.Id))
                {
                    p.Dispose();
                    continue;
                }

                bool match;
                try
                {
                    var path = p.MainModule?.FileName;
                    // 精确路径匹配；拿不到 MainModule 时仅当进程 image name 与目标一致才保守命中，
                    // 避免一棍子打死所有"模块不可读"的系统进程。
                    if (path != null)
                    {
                        match = Path.GetFullPath(path).Equals(targetPath, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        match = string.Equals(p.ProcessName, _options.MainProcessName, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Access denied 等：按 image name 兜底；不再无条件命中，避免误杀系统进程
                    try
                    {
                        match = string.Equals(p.ProcessName, _options.MainProcessName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        match = false;
                    }
                }

                if (match)
                {
                    list.Add(p);
                    seenPids.Add(p.Id);
                }
                else
                {
                    p.Dispose();
                }
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

        /// <summary>
        /// 找出 zip 条目中最长的公共目录前缀，以便解压时剥离。
        /// 例如 entries = ["publish/Merged/a.exe", "publish/Merged/sub/b.dll"]
        /// → 返回 "publish/Merged/"
        /// </summary>
        private static string? FindCommonRoot(ZipArchive zip)
        {
            string? common = null;
            foreach (var entry in zip.Entries)
            {
                var full = entry.FullName;
                if (string.IsNullOrEmpty(full)) continue;
                full = full.TrimEnd('/');

                if (common == null)
                {
                    common = string.IsNullOrEmpty(entry.Name)
                        ? full
                        : (Path.GetDirectoryName(full)?.Replace('\\', '/') ?? "");
                    continue;
                }

                while (common.Length > 0 && !full.StartsWith(common, StringComparison.OrdinalIgnoreCase))
                {
                    var idx = common.LastIndexOf('/');
                    common = idx >= 0 ? common[..idx] : "";
                }
                if (common == "") break;
            }

            return string.IsNullOrEmpty(common) ? null : common + "/";
        }

        /// <summary>剥离公共根目录前缀，无前缀则返回原名。</summary>
        private static string StripRoot(string entryName, string? rootPrefix)
        {
            if (rootPrefix != null && entryName.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return entryName[rootPrefix.Length..];
            return entryName;
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
