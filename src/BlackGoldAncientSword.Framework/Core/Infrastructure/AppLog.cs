using System;
using System.Diagnostics;
using System.IO;
#if !DEBUG
using Serilog;
using Serilog.Core;
using Serilog.Events;
#endif

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 应用级本地日志门面，全项目统一入口：业务层的每个 catch 只需写一行
    /// <c>AppLog.Error(ex, source)</c>，无需再手写 <see cref="Debug.WriteLine"/>。
    /// 输出行为由条件编译在本类内部统一切换：
    /// <list type="bullet">
    ///   <item><b>DEBUG</b> 构建：转发到 <see cref="Debug.WriteLine"/>，走 VS 输出窗口，
    ///         不产生任何文件 IO —— 方便调试期即时查看。</item>
    ///   <item><b>RELEASE</b> 构建：写入本地日志缓存目录（默认 我的文档\BlackGoldAncientSword\logs），
    ///         按天滚动 + 单文件上限。写入经 <see cref="!:Serilog.Sinks.Async"/> 包裹，落盘在后台线程完成，
    ///         调用方（含 UI 线程）不会因磁盘 IO 卡顿；任何日志内部异常都被吞掉，绝不影响业务逻辑。</item>
    /// </list>
    /// <para>用法：App 启动读到配置后调用一次 <see cref="Initialize"/> 指定日志目录；此前的调用会安全丢弃。</para>
    /// </summary>
    public static class AppLog
    {
        /// <summary>当前生效的日志目录（Release 下有值，DEBUG 下恒为 null）。</summary>
        public static string? LogDirectory { get; private set; }

#if !DEBUG
        private static Logger? _logger;
        private static readonly object _gate = new();
#endif

        /// <summary>
        /// 初始化日志到指定目录。可重复调用（改路径时重建）；传入空/无效路径则回退默认目录。
        /// DEBUG 下为空操作。此方法自身绝不抛异常。
        /// </summary>
        public static void Initialize(string? logDirectory)
        {
#if !DEBUG
            try
            {
                var dir = string.IsNullOrWhiteSpace(logDirectory) ? DefaultLogDirectory() : logDirectory!;
                Directory.CreateDirectory(dir);

                var newLogger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    // Async 包裹：日志事件先入内存队列，由独立后台线程批量落盘，调用线程零阻塞。
                    .WriteTo.Async(a => a.File(
                        path: Path.Combine(dir, "app-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: false,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
                    .CreateLogger();

                Logger? old;
                lock (_gate)
                {
                    old = _logger;
                    _logger = newLogger;
                    LogDirectory = dir;
                }
                old?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AppLog)}.{nameof(Initialize)}] {ex}");
            }
#endif
        }

        /// <summary>记录错误（含异常堆栈）。<paramref name="source"/> 建议用 <c>$"{nameof(X)}.{nameof(Y)}"</c> 定位。</summary>
        public static void Error(Exception ex, string source, string? message = null)
        {
#if DEBUG
            Debug.WriteLine($"[{source}] {Compose(message, ex)}");
#else
            Write(LogEventLevel.Error, ex, source, message);
#endif
        }

        /// <summary>记录错误（无异常对象）。</summary>
        public static void Error(string source, string message)
        {
#if DEBUG
            Debug.WriteLine($"[{source}] {message}");
#else
            Write(LogEventLevel.Error, null, source, message);
#endif
        }

        public static void Warning(string source, string message)
        {
#if DEBUG
            Debug.WriteLine($"[{source}] {message}");
#else
            Write(LogEventLevel.Warning, null, source, message);
#endif
        }

        public static void Info(string source, string message)
        {
#if DEBUG
            Debug.WriteLine($"[{source}] {message}");
#else
            Write(LogEventLevel.Information, null, source, message);
#endif
        }

        /// <summary>刷新并释放日志（进程退出前调用，确保 Async 队列落盘）。DEBUG 下为空操作。</summary>
        public static void Shutdown()
        {
#if !DEBUG
            Logger? old;
            lock (_gate)
            {
                old = _logger;
                _logger = null;
            }
            old?.Dispose();
#endif
        }

        /// <summary>
        /// 清空 <paramref name="logDirectory"/> 目录下的所有日志文件（<c>*.log</c>），保留目录本身。
        /// Release 下会先释放当前日志文件句柄再删除，删完自动用同一目录重启日志，保证后续仍可写；
        /// 传入空目录则回退到当前生效目录（<see cref="LogDirectory"/>）。此方法自身绝不抛异常，
        /// 全部 IO 卸载到线程池，不阻塞调用线程。
        /// </summary>
        public static async System.Threading.Tasks.Task ClearLogsAsync(string? logDirectory = null)
        {
            var dir = string.IsNullOrWhiteSpace(logDirectory) ? LogDirectory : logDirectory;
            if (string.IsNullOrWhiteSpace(dir))
                return;

#if !DEBUG
            // 释放 Serilog 对 app-*.log 的独占句柄，否则删除会因文件占用失败。
            Shutdown();
#endif
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var file in Directory.EnumerateFiles(dir!, "*.log"))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex)
                        {
                            // 单个文件删不掉（可能被外部工具打开）不阻断其余文件清理。
                            Debug.WriteLine($"[{nameof(AppLog)}.{nameof(ClearLogsAsync)}] delete {file} failed: {ex.Message}");
                        }
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
#if !DEBUG
                // 删完立即用同一目录重建 logger，保证清空后应用仍能继续记日志。
                Initialize(dir);
#endif
            }
        }

#if DEBUG
        private static string Compose(string? message, Exception ex) =>
            string.IsNullOrEmpty(message) ? ex.ToString() : $"{message}: {ex}";
#else
        private static void Write(LogEventLevel level, Exception? ex, string source, string? message)
        {
            try
            {
                var logger = _logger;
                if (logger is null) return;
                var text = string.IsNullOrEmpty(message) ? $"[{source}]" : $"[{source}] {message}";
                if (ex is null)
                    logger.Write(level, "{LogText}", text);
                else
                    logger.Write(level, ex, "{LogText}", text);
            }
            catch (Exception writeEx)
            {
                Debug.WriteLine($"[{nameof(AppLog)}.{nameof(Write)}] {writeEx}");
            }
        }

        private static string DefaultLogDirectory() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BlackGoldAncientSword", "logs");
#endif
    }
}
