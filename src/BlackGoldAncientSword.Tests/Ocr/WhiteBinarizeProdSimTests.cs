using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 模拟生产改造：把 <see cref="TeamInfoOcrService.CropAndBinarizeWhite"/> 换成
/// 白字二值化版本 (min(B,G,R) &gt;= 200 → 白字→黑,其余→白)，
/// 在所有可用的 team info 截图上和生产反色对比，逐 region 检查是否有回退。
/// </summary>
public class WhiteBinarizeProdSimTests
{
    private static readonly OcrRegion[] TrioRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };

    private static readonly OcrRegion[] DuoRegions = new[]
    {
        new OcrRegion { X = 0.386719, Y = 0.908750, Width = 0.110156, Height = 0.035000 },
        new OcrRegion { X = 0.557813, Y = 0.908750, Width = 0.117969, Height = 0.035000 },
    };

    public static IEnumerable<object[]> TrioImages => new[]
    {
        new object[] { "hero_selection_team.png" },
        new object[] { "hero_selection_team_02.png" },
        new object[] { "team_full_1.png" },
    };

    public static IEnumerable<object[]> DuoImages => new[]
    {
        new object[] { "duo_screenshot.png" },
        new object[] { "duo_team_selection.png" },
    };

    [Theory]
    [MemberData(nameof(TrioImages))]
    public async Task Trio_Compare_ProdInvert_vs_WhiteBinarize(string fileName)
        => await CompareRegions(fileName, TrioRegions, new[] { "L", "M", "R" });

    [Theory]
    [MemberData(nameof(DuoImages))]
    public async Task Duo_Compare_ProdInvert_vs_WhiteBinarize(string fileName)
        => await CompareRegions(fileName, DuoRegions, new[] { "L", "R" });

    private static async Task CompareRegions(string fileName, OcrRegion[] regions, string[] labels)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        Assert.True(File.Exists(srcPath), $"源图不存在: {srcPath}");

        var (rawBgra, w, h) = LoadBgra(srcPath);

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, $"whitebin_sim_{Path.GetFileNameWithoutExtension(fileName)}");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine($"=== {fileName}  ({w}x{h}) ===");

        int prodWinCount = 0, newWinCount = 0, tieCount = 0;

        for (int i = 0; i < regions.Length; i++)
        {
            var region = regions[i];
            int cx = (int)(region.X * w), cy = (int)(region.Y * h);
            int cw = (int)(region.Width * w), ch = (int)(region.Height * h);

            var (prodBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(rawBgra, w, h, region);
            var newPng = WhiteBinarizeToPng(rawBgra, w, cx, cy, cw, ch, 200);

            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_prod.bmp"), prodBmp!);
            File.WriteAllBytes(Path.Combine(outDir, $"{labels[i]}_new.png"), newPng);

            var prodRes = await engine.RecognizeAsync(prodBmp!);
            var newRes = await engine.RecognizeAsync(newPng);

            var prodText = string.Join("", prodRes.Select(r => r.Text));
            var newText = string.Join("", newRes.Select(r => r.Text));

            sb.AppendLine($"---- {labels[i]} region {cw}x{ch} ----");
            sb.AppendLine($"  生产反色   [{prodRes.Count} block]: '{prodText}'");
            foreach (var r in prodRes)
                sb.AppendLine($"    · '{r.Text}' conf={r.Confidence:F3}");
            sb.AppendLine($"  白字二值化 [{newRes.Count} block]: '{newText}'");
            foreach (var r in newRes)
                sb.AppendLine($"    · '{r.Text}' conf={r.Confidence:F3}");

            string verdict;
            if (prodText == newText) { verdict = "═══ 一致"; tieCount++; }
            else if (newRes.Count == 1 && prodRes.Count > 1) { verdict = "▲ 白二值化更干净（幻影消失）"; newWinCount++; }
            else if (prodRes.Count == 1 && newRes.Count > 1) { verdict = "▼ 白二值化多识（回归）"; prodWinCount++; }
            else { verdict = "≠ 不同，需人眼判"; }
            sb.AppendLine($"  → {verdict}");
        }

        sb.AppendLine($"小结: 白胜={newWinCount}  产胜={prodWinCount}  平={tieCount}");
        Console.WriteLine(sb.ToString());
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 白字二值化 + 反色（一步到位）：
    ///   min(B,G,R) &gt;= threshold → 判为白字 → 输出 0x00 (反色后黑字)
    ///   其余                       → 判为背景/干扰 → 输出 0xFF (反色后白底)
    /// </summary>
    private static byte[] WhiteBinarizeToPng(
        byte[] rawBgra, int fullW, int x, int y, int cw, int ch, int threshold)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = rawBgra[srcOff + col * 4 + 0];
                byte G = rawBgra[srcOff + col * 4 + 1];
                byte R = rawBgra[srcOff + col * 4 + 2];
                byte v = Math.Min(B, Math.Min(G, R)) >= threshold ? (byte)0x00 : (byte)0xFF;
                buf[dstOff + col * 4 + 0] = v;
                buf[dstOff + col * 4 + 1] = v;
                buf[dstOff + col * 4 + 2] = v;
                buf[dstOff + col * 4 + 3] = 0xFF;
            }
        }

        var info = new SKImageInfo(cw, ch, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var sk = new SKBitmap();
        var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try
        {
            sk.InstallPixels(info, handle.AddrOfPinnedObject(), cw * 4);
            using var img = SKImage.FromBitmap(sk);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally { handle.Free(); }
    }

    private static (byte[] raw, int w, int h) LoadBgra(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        var cv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = cv.PixelWidth, h = cv.PixelHeight;
        var buf = new byte[w * 4 * h];
        cv.CopyPixels(buf, w * 4, 0);
        return (buf, w, h);
    }
}
