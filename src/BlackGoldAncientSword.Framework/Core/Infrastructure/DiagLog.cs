using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BlackGoldAncientSword.Framework.Core.Infrastructure
{
    /// <summary>
    /// 排障诊断日志。写到 %LOCALAPPDATA%\BlackGoldAncientSword\monitor-diag.log，
    /// 提供带 tag 的行式追加输出，供开发/回归时定位事件时序、状态机决策等运行时线索。
    /// <para>
    /// <see cref="Write"/> 用 <see cref="ConditionalAttribute"/>("DEBUG") 修饰：
    /// Release 编译下所有调用点会被编译器整体剥离，Release 用户运行时不会创建
    /// 任何文件、不会产生 IO 开销、参数表达式也不会被求值。因此可放心在热路径散布调用。
    /// </para>
    /// </summary>
    public static class DiagLog
    {
        private static readonly object _lock = new();
        private static readonly string _path = InitPath();

        private static string InitPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlackGoldAncientSword");
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "monitor-diag.log");
        }

        /// <summary>
        /// 追加一行日志：<c>[HH:mm:ss.fff] [tag] msg</c>。
        /// Release 下调用点被编译器剥离，运行时无副作用。
        /// </summary>
        [Conditional("DEBUG")]
        public static void Write(string tag, string msg)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {msg}\n";
                lock (_lock)
                {
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
