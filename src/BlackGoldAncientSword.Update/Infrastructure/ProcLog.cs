using System;
using System.IO;
using System.Text.Json;
using Serilog;
using Serilog.Core;

namespace BlackGoldAncientSword.Update.Infrastructure
{
    /// <summary>
    /// 更新进程专用本地日志。<b>本进程与主程序刻意隔离、不引用 Framework，故不能用主程序的 AppLog</b>，
    /// 这里内联一份最小 Serilog 封装，写入与主程序<b>同一日志目录</b>（我的文档\BlackGoldAncientSword\logs），
    /// 文件名前缀 <c>update-</c> 以便与主程序 <c>app-</c> 日志区分。
    /// <para>
    /// 更新进程是排查"更新失败/卡住"的唯一现场，故不分 Debug/Release 一律落盘。
    /// 写入经 Async 包裹，不阻塞 UI；进程通过 <see cref="!:Environment.Exit"/> 硬退出，
    /// 必须在退出前调用 <see cref="Flush"/> 确保队列落盘。
    /// </para>
    /// </summary>
    public static class ProcLog
    {
        private static Logger? _logger;
        private static readonly object _gate = new();

        public static void Initialize()
        {
            try
            {
                var dir = ResolveLogDirectory();
                Directory.CreateDirectory(dir);

                var logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Async(a => a.File(
                        path: Path.Combine(dir, "update-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: false,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
                    .CreateLogger();

                lock (_gate) { _logger = logger; }
            }
            catch
            {
                // 日志初始化失败绝不能拖垮更新流程。
            }
        }

        public static void Info(string source, string message) => Write(l => l.Information("[{Source}] {Msg}", source, message));

        public static void Warning(string source, string message) => Write(l => l.Warning("[{Source}] {Msg}", source, message));

        public static void Error(Exception ex, string source, string? message = null) =>
            Write(l => l.Error(ex, "[{Source}] {Msg}", source, message ?? string.Empty));

        /// <summary>刷新 Async 队列并释放句柄。进程 Environment.Exit 前<b>必须</b>调用，否则丢日志。</summary>
        public static void Flush()
        {
            Logger? old;
            lock (_gate) { old = _logger; _logger = null; }
            old?.Dispose();
        }

        private static void Write(Action<ILogger> act)
        {
            try
            {
                var logger = _logger;
                if (logger is not null) act(logger);
            }
            catch { }
        }

        /// <summary>
        /// 解析日志目录：优先读主程序 settings.json 里用户设置的 LogPath，读不到/无效则回退默认目录。
        /// <para>
        /// settings.json 位于固定位置 <c>我的文档\BlackGoldAncientSword\settings.json</c>——该路径用主程序的
        /// <c>AppSettings.GetDefaultPath()</c> 拼接，<b>不受用户改动 LogPath/DataPath 影响</b>，故独立进程能稳定读到。
        /// 这样用户在设置页改了日志路径后，更新器也会写到同一个新目录，而非写死的默认目录。
        /// </para>
        /// </summary>
        private static string ResolveLogDirectory()
        {
            var defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BlackGoldAncientSword", "logs");
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "BlackGoldAncientSword", "settings.json");
                if (!File.Exists(settingsPath))
                    return defaultDir;

                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                // 主程序序列化未设 PropertyNamingPolicy，键为帕斯卡 "LogPath"；用忽略大小写查找更稳。
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("LogPath") || string.Equals(prop.Name, "LogPath", StringComparison.OrdinalIgnoreCase))
                    {
                        var logPath = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(logPath))
                            return logPath!;
                    }
                }
            }
            catch
            {
                // settings.json 缺失/损坏/权限问题都回退默认目录，绝不因读配置失败而丢日志能力。
            }
            return defaultDir;
        }
    }
}
