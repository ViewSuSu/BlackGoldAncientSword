using System.Diagnostics;
using Microsoft.Win32;

namespace BlackGoldAncientSword.GameMonitor.Services.Implementation.Internal
{
    /// <summary>
    /// 解析永劫无间 Steam / 网易客户端的安装路径，进而定位 CCMini 日志目录。
    /// <para>
    /// 完全从注册表 / Steam 库配置读取，不写死任何安装路径——别人的电脑可能把游戏装在
    /// 其它盘或非标准目录，写死路径必然找不到。
    /// </para>
    /// <para>
    /// Steam: 从 <c>HKLM\SOFTWARE\WOW6432Node\Valve\Steam</c> 的 InstallPath 拿 Steam 根，
    /// 游戏在 <c>&lt;SteamRoot&gt;\steamapps\common\NARAKA BLADEPOINT</c>；卸载项
    /// <c>Steam App 1203220</c> 的 InstallLocation 更精确；多库场景再解析
    /// <c>libraryfolders.vdf</c> 枚举所有库路径。
    /// </para>
    /// <para>
    /// 网易: 卸载项 <c>Naraka</c> 的 DisplayIcon / UninstallString 指向
    /// <c>&lt;根&gt;\tool\*.exe</c>，去掉 <c>tool</c> 目录即得网易根，游戏在 <c>&lt;根&gt;\program</c>。
    /// </para>
    /// </summary>
    internal static class GameInstallLocator
    {
        // Steam 卸载项键名固定为 "Steam App <AppId>"，永劫无间 AppId = 1203220。
        private const string SteamNarakaAppId = "1203220";
        private const string SteamUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamNarakaAppId;
        private const string SteamUninstallKeyWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamNarakaAppId;
        private const string SteamValveKey = @"SOFTWARE\WOW6432Node\Valve\Steam";
        private const string SteamValveKeyNative = @"SOFTWARE\Valve\Steam";
        private const string NeteaseUninstallKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Naraka";
        private const string NeteaseUninstallKeyNative = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Naraka";

        private const string CcMiniRel = "ccmini"; // <游戏根>\ccmini\ccmini_new\logs（Steam 与网易共用该相对结构）

        /// <summary>
        /// 返回所有能解析出的 CCMini 日志目录（Steam + 网易各一个，装了哪个返回哪个）。
        /// 每个返回值都是已确认存在的目录；解析失败 / 目录不存在的一律跳过。
        /// </summary>
        public static IReadOnlyList<string> ResolveAllCcMiniLogDirs()
        {
            var dirs = new List<string>();

            // Steam 端（<游戏根>\ccmini\ccmini_new\logs）
            var steamRoot = ResolveSteamNarakaRoot();
            if (steamRoot != null)
            {
                var d = Path.Combine(steamRoot, CcMiniRel, "ccmini_new", "logs");
                if (Directory.Exists(d)) dirs.Add(d);
            }

            // 网易端（<网易根>\program\ccmini\ccmini_new\logs）
            var neteaseRoot = ResolveNeteaseNarakaRoot();
            if (neteaseRoot != null)
            {
                var d = Path.Combine(neteaseRoot, "program", CcMiniRel, "ccmini_new", "logs");
                if (Directory.Exists(d)) dirs.Add(d);
            }

            return dirs;
        }

        /// <summary>
        /// 解析 Steam 版永劫无间根目录（含 NarakaBladepoint.exe 的目录）。
        /// 优先级：卸载项 InstallLocation → Valve\Steam 根 + common 子路径 → libraryfolders.vdf 枚举。
        /// </summary>
        private static string? ResolveSteamNarakaRoot()
        {
            // 1) 卸载项 InstallLocation（最精确，直接给完整游戏目录）。
            var uninst = RegistryHelper.GetString(
                RegistryHelper.OpenAnySubKey(SteamUninstallKey, SteamUninstallKeyWow), "InstallLocation");
            if (!string.IsNullOrWhiteSpace(uninst))
                return EnsureHasExe(uninst);

            // 2) Valve\Steam 根 InstallPath + steamapps\common\NARAKA BLADEPOINT。
            var steamRoot = RegistryHelper.GetString(
                RegistryHelper.OpenAnySubKey(SteamValveKey, SteamValveKeyNative), "InstallPath");
            if (!string.IsNullOrWhiteSpace(steamRoot))
            {
                var candidate = Path.Combine(steamRoot, "steamapps", "common", "NARAKA BLADEPOINT");
                if (EnsureHasExe(candidate) != null) return candidate;
            }

            // 3) 多库：解析 <SteamRoot>\steamapps\libraryfolders.vdf，枚举每个库的 common 路径。
            if (!string.IsNullOrWhiteSpace(steamRoot))
            {
                var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                foreach (var lib in SteamLibraryParser.ParseLibraryPaths(vdf))
                {
                    var candidate = Path.Combine(lib, "steamapps", "common", "NARAKA BLADEPOINT");
                    if (EnsureHasExe(candidate) != null) return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 解析网易版永劫无间根目录（含 program 子目录的根，如 C:\Naraka）。
        /// 从卸载项 DisplayIcon / UninstallString 推导：二者都指向 <根>\tool\*.exe。
        /// </summary>
        private static string? ResolveNeteaseNarakaRoot()
        {
            var key = RegistryHelper.OpenAnySubKey(NeteaseUninstallKey, NeteaseUninstallKeyNative);
            var root = RegistryHelper.GetString(key, "DisplayIcon");
            if (string.IsNullOrWhiteSpace(root))
                root = RegistryHelper.GetString(key, "UninstallString");
            if (string.IsNullOrWhiteSpace(root)) return null;

            // 取第一个可执行/可执行工具所在的父目录的父目录：<根>\tool\xxx.exe → 根。
            var dir = Path.GetDirectoryName(root);
            if (string.IsNullOrEmpty(dir)) return null;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent)) return null;
            return Directory.Exists(parent) ? parent : null;
        }

        /// <summary>
        /// 确认目录内含 NarakaBladepoint.exe 才返回，否则返回 null。
        /// 避免注册表残留指向已移动的目录。
        /// </summary>
        private static string? EnsureHasExe(string root)
        {
            if (!Directory.Exists(root)) return null;
            return File.Exists(Path.Combine(root, "NarakaBladepoint.exe")) ? root : null;
        }

        /// <summary>
        /// 简单注册表读取辅助：兼容 WOW6432Node 与原生视图，避免 32/64 位进程差异。
        /// </summary>
        private static class RegistryHelper
        {
            public static RegistryKey? OpenAnySubKey(params string[] subKeys)
            {
                foreach (var sub in subKeys)
                {
                    if (string.IsNullOrEmpty(sub)) continue;
                    try
                    {
                        var key = Registry.LocalMachine.OpenSubKey(sub, writable: false);
                        if (key != null) return key;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                    {
                        // 某些键不存在会抛；忽略继续尝试其它键。
                    }
                }
                return null;
            }

            public static string? GetString(RegistryKey? key, string valueName)
            {
                if (key == null) return null;
                try { return key.GetValue(valueName) as string; }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 解析 Steam libraryfolders.vdf，提取所有库路径（含主库）。
        /// </summary>
        private static class SteamLibraryParser
        {
            public static IEnumerable<string> ParseLibraryPaths(string vdfPath)
            {
                if (!File.Exists(vdfPath)) yield break;
                string[] lines;
                try { lines = File.ReadAllLines(vdfPath); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    yield break;
                }

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
                    var val = line.Substring("\"path\"".Length).Trim().Trim('"');
                    if (!string.IsNullOrEmpty(val))
                        yield return val.Replace("\\\\", "\\");
                }
            }
        }
    }
}
