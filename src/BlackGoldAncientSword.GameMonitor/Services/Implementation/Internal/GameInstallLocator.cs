using System.Text;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using Microsoft.Win32;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal
{
    /// <summary>
    /// 从游戏本体写入 HKCU 的 PlayerPrefs 定位 CCMini 日志目录。
    /// <para>
    /// 永劫无间每次启动都会把当前安装路径重写到
    /// <c>HKCU\Software\24Entertainment\Naraka</c> 下的
    /// <c>install_path_steam_*</c> / <c>install_path_netease_*</c>（Unity PlayerPrefs，REG_BINARY）。
    /// 它反映运行时的实况，比 HKLM 卸载项（安装时刻快照，搬动/重装后残留旧值）可靠得多，
    /// 因此是唯一路径来源，不再从卸载项 / Valve 键 / libraryfolders.vdf 推导。
    /// </para>
    /// <para>
    /// 两个键都指向"含 ccmini 的那一层"（Steam=<游戏根>/、网易=<游戏根>\program/），
    /// 统一只拼一层 <c>ccmini\ccmini_new\logs</c>。
    /// </para>
    /// </summary>
    internal static class GameInstallLocator
    {
        private const string GamePlayerPrefsKey = @"Software\24Entertainment\Naraka";
        private const string SteamPathPrefix = "install_path_steam_";
        private const string NeteasePathPrefix = "install_path_netease_";

        private const string CcMiniRel = "ccmini"; // <游戏根>\ccmini\ccmini_new\logs（Steam 与网易共用该相对结构）

        /// <summary>
        /// 返回所有能解析出的 CCMini 日志目录（Steam + 网易各一个，装了哪个返回哪个）。
        /// 每个返回值都是已确认存在的目录；解析失败 / 目录不存在的一律跳过。
        /// </summary>
        public static IReadOnlyList<string> ResolveAllCcMiniLogDirs()
        {
            var dirs = new List<string>();
            var failures = new List<string>();

            foreach (var d in ResolveFromGamePlayerPrefs(failures))
            {
                if (Directory.Exists(d))
                {
                    dirs.Add(d);
                    AppLog.Info(nameof(GameInstallLocator), $"HKCU 游戏自记路径命中: {d}");
                }
                else
                {
                    failures.Add($"HKCU 路径目录不存在: {d}");
                }
            }

            // 诊断汇总：解析失败也要把原因落日志，避免"目录明明在却静默跳过"无从排查。
            if (dirs.Count == 0 && failures.Count > 0)
                AppLog.Warning(nameof(GameInstallLocator), $"所有 CCMini 日志目录解析均失败: {string.Join("; ", failures)}");
            else if (failures.Count > 0)
                AppLog.Warning(nameof(GameInstallLocator), $"部分 CCMini 日志目录解析失败: {string.Join("; ", failures)}");

            return dirs;
        }

        /// <summary>
        /// 从 HKCU 游戏 PlayerPrefs 解析 CCMini 日志目录（install_path_steam_* / install_path_netease_*）。
        /// 值类型为 REG_BINARY（UTF-8 字节 + 尾部 \0）。
        /// </summary>
        private static List<string> ResolveFromGamePlayerPrefs(List<string> failures)
        {
            var dirs = new List<string>();
            RegistryKey? key;
            try
            {
                key = Registry.CurrentUser.OpenSubKey(GamePlayerPrefsKey, writable: false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add($"打开 HKCU\\{GamePlayerPrefsKey} 失败: {ex.Message}");
                return dirs;
            }

            if (key == null)
            {
                failures.Add($"HKCU\\{GamePlayerPrefsKey} 不存在");
                return dirs;
            }

            using (key)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    string? installLayer = null;
                    if (valueName.StartsWith(SteamPathPrefix, StringComparison.OrdinalIgnoreCase))
                        installLayer = "Steam";
                    else if (valueName.StartsWith(NeteasePathPrefix, StringComparison.OrdinalIgnoreCase))
                        installLayer = "Netease";
                    else
                        continue;

                    var path = DecodePlayerPrefsPath(key, valueName, failures);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        failures.Add($"{valueName} 解码为空");
                        continue;
                    }
                    var d = Path.Combine(path, CcMiniRel, "ccmini_new", "logs");
                    AppLog.Info(nameof(GameInstallLocator), $"{installLayer} 游戏自记路径 {valueName} => {path}");
                    dirs.Add(d);
                }
            }
            return dirs;
        }

        /// <summary>
        /// 解码 Unity PlayerPrefs 的 REG_BINARY 字符串值（UTF-8 字节 + 尾部 \0）。
        /// 返回纯字符串（如 C:/Naraka/program/），解码失败返回 null。
        /// </summary>
        private static string? DecodePlayerPrefsPath(RegistryKey key, string valueName, List<string> failures)
        {
            try
            {
                var raw = key.GetValue(valueName);
                if (raw is not byte[] bytes || bytes.Length == 0)
                {
                    failures.Add($"{valueName} 值类型异常(非 REG_BINARY/空)");
                    return null;
                }
                // 去掉尾部 \0 再按 UTF-8 解码。
                var end = Array.IndexOf(bytes, (byte)0);
                if (end < 0) end = bytes.Length;
                return Encoding.UTF8.GetString(bytes, 0, end);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                failures.Add($"解码 {valueName} 失败: {ex.Message}");
                return null;
            }
        }
    }
}
