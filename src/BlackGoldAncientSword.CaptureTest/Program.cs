using System.Diagnostics;
using System.Text;
using BlackGoldAncientSword.ScreenCapture;

// ============================================================
//  永劫无间 截图测试工具
//  逻辑与主程序完全一致：直接复用 ScreenCaptureService，
//  走 Native WGC -> COM WGC -> GDI 三级降级链路。
//  成功 -> 桌面生成 永劫截图测试.png
//  失败 -> 桌面生成 永劫截图测试_错误日志.txt
// ============================================================

const string ProcessName = "NarakaBladepoint";

var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
var pngPath = Path.Combine(desktop, "永劫截图测试.png");
var logPath = Path.Combine(desktop, "永劫截图测试_错误日志.txt");

var log = new List<string>();
void L(string m) => log.Add($"[{DateTime.Now:HH:mm:ss.fff}] {m}");

void WriteErrorLog(string? extra)
{
    var sb = new StringBuilder();
    sb.AppendLine("永劫无间 截图测试 - 错误日志");
    sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"系统: {Environment.OSVersion.VersionString}");
    sb.AppendLine($"64位系统: {Environment.Is64BitOperatingSystem}");
    sb.AppendLine($".NET: {Environment.Version}");
    sb.AppendLine("--------------------------------------------");
    foreach (var line in log) sb.AppendLine(line);
    if (!string.IsNullOrEmpty(extra))
    {
        sb.AppendLine("--------------------------------------------");
        sb.AppendLine("异常详情:");
        sb.AppendLine(extra);
    }
    File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(false));
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine();
Console.WriteLine("  ============================================");
Console.WriteLine("   永劫无间 截图测试");
Console.WriteLine("  ============================================");
Console.WriteLine("   请确保游戏已打开并停留在游戏画面");
Console.WriteLine();
Console.WriteLine("   正在截图，请稍候...");
Console.WriteLine();

int exitCode;
using var capture = new ScreenCaptureService();
try
{
    L($"查找游戏进程: {ProcessName}");
    var procs = Process.GetProcessesByName(ProcessName);
    try
    {
        if (procs.Length == 0)
        {
            L("未找到进程，游戏可能未启动");
            WriteErrorLog(null);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [失败] 未检测到游戏进程，请确认游戏已启动。日志已保存到桌面。");
            Console.ResetColor();
            return Finish(1);
        }
    }
    finally { foreach (var p in procs) p.Dispose(); }

    if (!capture.TryFindGameWindow(ProcessName, out var hwnd) || hwnd == IntPtr.Zero)
    {
        L("进程存在但拿不到有效主窗口句柄（可能最小化或无窗口）");
        WriteErrorLog(null);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  [失败] 游戏进程存在但拿不到窗口，请让游戏处于前台。日志已保存到桌面。");
        Console.ResetColor();
        return Finish(1);
    }
    L($"找到窗口句柄: 0x{hwnd:X}");

    // 与主程序完全一致的截图调用：三级降级链路全部在 ScreenCaptureService 内部
    L("开始截图 (Native WGC -> COM WGC -> GDI 降级链路)");
    var pngBytes = capture.CaptureGame(ProcessName);
    L($"截图返回 {pngBytes.Length} 字节");

    if (File.Exists(pngPath)) File.Delete(pngPath);
    File.WriteAllBytes(pngPath, pngBytes);
    L($"PNG 已保存: {pngPath}");

    // 黑屏检测：GDI 兜底对独占全屏经常截出黑图
    double brightRatio = EstimateBrightRatio(pngBytes, out int sampled, out int bright);
    L($"亮度采样: {bright}/{sampled} 非黑像素占比 {brightRatio:F3}");

    if (brightRatio < 0.02)
    {
        L("警告: 画面几乎全黑。独占全屏/受保护画面 GDI 兜底通常无效，建议切换【无边框窗口】模式重试。");
        WriteErrorLog(null);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  [警告] 截到的图几乎全黑，可能是全屏独占模式。请把游戏改成无边框窗口模式后重试。");
        Console.WriteLine($"  图片仍已保存: {pngPath}");
        Console.ResetColor();
        return Finish(2);
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  [成功] 截图已保存到桌面: 永劫截图测试.png");
    Console.ResetColor();
    exitCode = 0;
}
catch (Exception ex)
{
    L($"发生异常: {ex.GetType().FullName}");
    WriteErrorLog(ex.ToString());
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("  [失败] 截图出错，错误日志已保存到桌面: 永劫截图测试_错误日志.txt");
    Console.ResetColor();
    exitCode = 1;
}

return Finish(exitCode);

int Finish(int code)
{
    Console.WriteLine();
    Console.WriteLine("  ============================================");
    Console.WriteLine(code switch
    {
        0 => "   测试完成：截图成功",
        2 => "   测试完成：疑似全屏黑屏，请看上方黄色提示",
        _ => "   测试完成：截图失败，请把桌面的错误日志txt发回",
    });
    Console.WriteLine("  ============================================");
    Console.WriteLine();
    Console.WriteLine("  按任意键退出...");
    try { Console.ReadKey(true); } catch { }
    return code;
}

// 从 PNG 解码后按网格采样，统计非黑像素占比
static double EstimateBrightRatio(byte[] pngBytes, out int sampled, out int bright)
{
    sampled = 0; bright = 0;
    using var ms = new MemoryStream(pngBytes);
    using var bmp = new System.Drawing.Bitmap(ms);
    int stepX = Math.Max(1, bmp.Width / 20);
    int stepY = Math.Max(1, bmp.Height / 20);
    for (int y = 0; y < bmp.Height; y += stepY)
    for (int x = 0; x < bmp.Width; x += stepX)
    {
        var c = bmp.GetPixel(x, y);
        sampled++;
        if (c.R + c.G + c.B > 30) bright++;
    }
    return sampled > 0 ? (double)bright / sampled : 0;
}
