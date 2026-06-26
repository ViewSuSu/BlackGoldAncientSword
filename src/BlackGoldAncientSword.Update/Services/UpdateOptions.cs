using System;
using System.IO;

namespace BlackGoldAncientSword.Update.Services
{
    /// <summary>
    /// 命令行参数：
    ///   --url       <zip 下载直链>   必填
    ///   --target    <目标安装目录>   选填，缺省=updater 所在目录
    ///   --main-exe  <主程序 exe 名>  选填，缺省=BlackGoldAncientSword.App.exe
    ///   --no-restart                 选填，结束后不重启主程序
    /// </summary>
    public sealed class UpdateOptions
    {
        public string ZipUrl { get; private set; } = string.Empty;

        public string TargetDirectory { get; private set; } =
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public string MainExeName { get; private set; } = "BlackGoldAncientSword.App.exe";

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
                    case "--no-restart":
                        opts.RestartAfterInstall = false;
                        break;
                }
            }
            return opts;
        }
    }
}
