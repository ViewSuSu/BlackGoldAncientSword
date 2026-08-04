using System.Diagnostics;
using System.IO;
using System.Text;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using BlackGoldAncientSword.ScreenCapture;

// ============================================================
//  永劫无间 截图 + OCR 识别测试工具
//  逻辑与主程序完全一致：
//    截图   -> ScreenCaptureService（Native WGC -> COM WGC -> GDI）
//    识别   -> TeamInfoOcrService.RecognizeTeamMembersAutoAsync
//              （自动判三排/双排 -> 白字二值化 -> PP-OCRv5 推理）
//  成功 -> 桌面生成 永劫截图识别测试.png + 控制台打印队伍文本
//  失败 -> 桌面生成 永劫截图识别测试_错误日志.txt
// ============================================================

const string ProcessName = "NarakaBladepoint";

var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
var pngPath = Path.Combine(desktop, "永劫截图识别测试.png");
var logPath = Path.Combine(desktop, "永劫截图识别测试_错误日志.txt");

// exe 自身所在目录，作为桌面写入失败时的兜底日志位置。
var exeDir = AppContext.BaseDirectory;

var log = new List<string>();
void L(string m) => log.Add($"[{DateTime.Now:HH:mm:ss.fff}] {m}");

// 尽最大努力把内容写到多个位置，任一成功即可，绝不因写日志失败而中断/静默退出。
void SafeWrite(string fileName, string content)
{
    foreach (var dir in new[] { desktop, exeDir })
    {
        try
        {
            File.WriteAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(false));
        }
        catch { /* 换下一个位置 */ }
    }
}

// 启动标记：只要 .NET 主机成功进入本程序，第一时间落一个文件。
// 若用户桌面连这个文件都没有，说明 exe 根本没跑起来（杀软拦截 / 文件损坏 / 运行时缺失），
// 与业务逻辑无关，应从"能否启动"方向排查。
SafeWrite("永劫截图识别测试_已启动.txt",
    $"程序已成功启动\r\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
    $"系统: {Environment.OSVersion.VersionString}\r\n" +
    $"64位系统: {Environment.Is64BitOperatingSystem}\r\n" +
    $".NET: {Environment.Version}\r\nexe目录: {exeDir}\r\n" +
    "若后续无 png 也无错误日志，请把本文件发回。");

void WriteErrorLog(string? extra)
{
    var sb = new StringBuilder();
    sb.AppendLine("永劫无间 截图+OCR识别测试 - 错误日志");
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
    SafeWrite("永劫截图识别测试_错误日志.txt", sb.ToString());
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine();
Console.WriteLine("  ============================================");
Console.WriteLine("   永劫无间 截图 + OCR 识别测试");
Console.WriteLine("  ============================================");
Console.WriteLine("   请确保游戏在【队伍信息界面】（能看到队友名字）");
Console.WriteLine();

int exitCode;
ScreenCaptureService? capture = null;
OcrEngine? ocrEngine = null;
try
{
    // 构造放进 try：OcrEngine 构造时会 ResolveModelDir()，模型缺失会当场抛异常，
    // 必须被 catch 兜住并落日志，否则会绕过错误日志静默退出。
    capture = new ScreenCaptureService();
    ocrEngine = new OcrEngine();

    // ── 1. 进程 / 窗口检查 ──
    L($"查找游戏进程: {ProcessName}");
    var procs = Process.GetProcessesByName(ProcessName);
    try
    {
        if (procs.Length == 0)
        {
            L("未找到进程，游戏可能未启动");
            WriteErrorLog(null);
            Fail("未检测到游戏进程，请确认游戏已启动。日志已保存到桌面。");
            return Finish(1);
        }
    }
    finally { foreach (var p in procs) p.Dispose(); }

    if (!capture.TryFindGameWindow(ProcessName, out var hwnd) || hwnd == IntPtr.Zero)
    {
        L("进程存在但拿不到有效主窗口句柄");
        WriteErrorLog(null);
        Fail("游戏进程存在但拿不到窗口，请让游戏处于前台。日志已保存到桌面。");
        return Finish(1);
    }
    L($"找到窗口句柄: 0x{hwnd:X}");

    // ── 2. 截图并保存 PNG（供人工核对）──
    Console.WriteLine("   [1/2] 正在截图...");
    var pngBytes = capture.CaptureGame(ProcessName);
    L($"截图返回 {pngBytes.Length} 字节");
    if (File.Exists(pngPath)) File.Delete(pngPath);
    File.WriteAllBytes(pngPath, pngBytes);
    L($"PNG 已保存: {pngPath}");
    Console.WriteLine($"         截图已保存: {pngPath}");

    // ── 3. OCR 预热 ──
    Console.WriteLine("   [2/2] 正在加载 OCR 模型并识别（首次较慢）...");
    var sw = Stopwatch.StartNew();
    await ocrEngine.PrewarmAsync();
    L($"OCR 预热完成 {sw.ElapsedMilliseconds} ms");

    // ── 4. 队伍识别（与主程序完全一致：自动判三排/双排）──
    var teamOcr = new TeamInfoOcrService(capture, ocrEngine);
    sw.Restart();
    var members = await teamOcr.RecognizeTeamMembersAutoAsync();
    L($"识别完成 {sw.ElapsedMilliseconds} ms，识别到 {members.Length} 名队友");

    // ── 5. 打印结果 ──
    Console.WriteLine();
    Console.WriteLine("  ============================================");
    if (members.Length == 0)
    {
        L("识别到 0 名队友：可能不在队伍界面 / 截图黑屏 / 区域不匹配");
        WriteErrorLog(null);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("   识别结果：未识别到任何队友名字");
        Console.WriteLine("   可能原因：");
        Console.WriteLine("     1. 当前不在能看到队友名字的界面");
        Console.WriteLine("     2. 截图是全屏独占黑屏（试试无边框窗口模式）");
        Console.WriteLine("     3. 游戏分辨率/UI 缩放导致识别区域不匹配");
        Console.WriteLine($"   请把桌面的 PNG 和日志发回排查: {pngPath}");
        Console.ResetColor();
        exitCode = 2;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"   识别到 {members.Length} 名队友：");
        Console.ResetColor();
        for (int i = 0; i < members.Length; i++)
        {
            L($"队友[{i + 1}] = {members[i]}");
            Console.WriteLine($"     {i + 1}. {members[i]}");
        }
        exitCode = 0;
    }
    Console.WriteLine("  ============================================");
}
catch (Exception ex)
{
    L($"发生异常: {ex.GetType().FullName}");
    WriteErrorLog(ex.ToString());
    Fail("识别过程出错，错误日志已保存到桌面: 永劫截图识别测试_错误日志.txt");
    exitCode = 1;
}
finally
{
    capture?.Dispose();
    ocrEngine?.Dispose();
}

return Finish(exitCode);

void Fail(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  [失败] {msg}");
    Console.ResetColor();
}

int Finish(int code)
{
    Console.WriteLine();
    Console.WriteLine(code switch
    {
        0 => "   测试完成：识别成功",
        2 => "   测试完成：截图成功但未识别到队友，请看上方提示",
        _ => "   测试完成：失败，请把桌面的错误日志txt发回",
    });
    Console.WriteLine();
    Console.WriteLine("  按任意键退出...");
    try { Console.ReadKey(true); } catch { }
    return code;
}
