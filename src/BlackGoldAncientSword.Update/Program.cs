using Avalonia;

namespace BlackGoldAncientSword.Update
{
    internal static class Program
    {
        [System.STAThread]
        public static int Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // Avalonia previewer / 设计期需要此方法
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
