using System;
using System.IO;

namespace BlackGoldAncientSword.Update.Services
{
    /// <summary>
    /// 命令行参数：
    ///   --url       <zip 下载直链>   必填
    ///   --target    <目标安装目录>   选填，缺省=updater 所在目录
    ///   --main-exe  <主程序 exe 名>  选填，缺省=BlackGoldAncientSword.App.exe
    ///   --main-pid  <主程序 PID>     选填，但强烈建议主程序传入：按 PID 关进程是最稳的方案，
    ///                                可绕开 image name 不一致（dotnet host / 多会话 / 同名异目录）的全部歧义
    ///   --no-restart                 选填，结束后不重启主程序
    /// </summary>
    public sealed class UpdateOptions
    {
        public string ZipUrl { get; private set; } = string.Empty;

        public string TargetDirectory { get; private set; } =
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public string MainExeName { get; private set; } = "BlackGoldAncientSword.App.exe";

        /// <summary>
        /// 主程序进程 PID。来自主程序 OnlineUpdateCommand 传入的 --main-pid 参数，
        /// null 表示主程序未传（兼容旧版主程序），UpdaterRunner 会退化到 image name 匹配。
        /// </summary>
        public int? MainPid { get; private set; }

        public bool RestartAfterInstall { get; private set; } = true;

        public string MainExeFullPath => Path.Combine(TargetDirectory, MainExeName);

        public string MainProcessName =>
            Path.GetFileNameWithoutExtension(MainExeName);

        public static UpdateOptions Parse(string[] args)
        {
            var opts = new UpdateOptions();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--url" when i + 1 < args.Length:
                        opts.ZipUrl = args[++i];
                        break;
                    case "--target" when i + 1 < args.Length:
                        opts.TargetDirectory = Path.GetFullPath(args[++i]);
                        break;
                    case "--main-exe" when i + 1 < args.Length:
                        opts.MainExeName = args[++i];
                        break;
                    case "--main-pid" when i + 1 < args.Length:
                        // 解析失败（非数字 / 负数）静默忽略，退回 name 匹配，保证流程不被错误参数中断
                        if (int.TryParse(args[++i], out var pid) && pid > 0)
                            opts.MainPid = pid;
                        break;
                    case "--no-restart":
                        opts.RestartAfterInstall = false;
                        break;
                }
            }
            return opts;
        }
    }
}
