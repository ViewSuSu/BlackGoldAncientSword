using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 最终方案 = 白字二值化 (min(B,G,R) &gt;= 200 → 白字→黑,其余→白) + 后置 conf &lt; 0.7 过滤。
/// 在所有可用测试图上跑一遍，与生产反色路径对比，一次给出完整结果表。
/// </summary>
public class FinalPipelineTests
{
    private static readonly OcrRegion[] TrioRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    private static readonly OcrRegion[] DuoRegions = new[]
    {
        new OcrRegion { X = 0.386719, Y = 0.906250, Width = 0.110547, Height = 0.040000 },
        new OcrRegion { X = 0.557813, Y = 0.906250, Width = 0.119141, Height = 0.040000 },
    };

    private const int WhiteThreshold = 200;
    private const double ConfCutoff = 0.7;

    [Fact]
    public async Task Final_Pipeline_AllImages_Summary()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        using var engine = new OcrEngine();

        var sb = new StringBuilder();
        sb.AppendLine($"方案 = 白字二值化(阈值={WhiteThreshold}) + rec conf<{ConfCutoff} 过滤");
        sb.AppendLine();

        // ─── team info 完整截图（trio & duo）───
        await RunFullShot(engine, "hero_selection_team.png",    TrioRegions, new[] { "L","M","R" }, sb);
        await RunFullShot(engine, "hero_selection_team_02.png", TrioRegions, new[] { "L","M","R" }, sb);
        await RunFullShot(engine, "team_full_1.png",            TrioRegions, new[] { "L","M","R" }, sb);
        await RunFullShot(engine, "duo_screenshot.png",         DuoRegions,  new[] { "L","R"     }, sb);
        await RunFullShot(engine, "duo_team_selection.png",     DuoRegions,  new[] { "L","R"     }, sb);

        // ─── 单块手动裁剪（无 region，整张图即 crop）───
        await RunPreCropped(engine, "special_char_1.png", sb);
        await RunPreCropped(engine, "special_char_2.png", sb);

        Console.WriteLine(sb.ToString());
    }

    private static async Task RunFullShot(
        OcrEngine engine, string fileName, OcrRegion[] regions, string[] labels, StringBuilder sb)
    {
        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        if (!File.Exists(srcPath))
        {
            sb.AppendLine($"[SKIP] {fileName} 不存在");
            return;
        }

        var (raw, w, h) = LoadBgra(srcPath);
        sb.AppendLine($"═══ {fileName}  ({w}x{h}) ═══");

        for (int i = 0; i < regions.Length; i++)
        {
            var reg = regions[i];
            int cx = (int)(reg.X * w), cy = (int)(reg.Y * h);
            int cw = (int)(reg.Width * w), ch = (int)(reg.Height * h);

            // 生产反色
            var (prodBmp, _, _) = TeamInfoOcrService.CropAndBinarizeWhite(raw, w, h, reg);
            var prodRes = await engine.RecognizeAsync(prodBmp!);
            var prodText = string.Join("", prodRes.Select(r => r.Text));

            // 最终方案：白二值化
            var newBytes = WhiteBinarize(raw, w, cx, cy, cw, ch, WhiteThreshold);
            var rawNewRes = await engine.RecognizeAsync(newBytes);
            var filtered = rawNewRes.Where(r => r.Confidence >= ConfCutoff).ToList();
            var newText = string.Join("", filtered.Select(r => r.Text));

            sb.AppendLine($"  {labels[i]} region {cw}x{ch}");
            sb.AppendLine($"    生产反色  → '{prodText}'  [{FormatBlocks(prodRes)}]");
            sb.AppendLine($"    最终方案  → '{newText}'  [过滤前 {rawNewRes.Count} block, 过滤后 {filtered.Count}]  [{FormatBlocks(rawNewRes)}]");
        }
        sb.AppendLine();
    }

    private static async Task RunPreCropped(OcrEngine engine, string fileName, StringBuilder sb)
    {
        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        if (!File.Exists(srcPath))
        {
            sb.AppendLine($"[SKIP] {fileName} 不存在");
            return;
        }

        var (raw, w, h) = LoadBgra(srcPath);
        sb.AppendLine($"═══ {fileName}  ({w}x{h}) 已裁剪小图 ═══");

        // 直接原图 OCR
        var origRes = await engine.RecognizeAsync(File.ReadAllBytes(srcPath));
        var origText = string.Join("", origRes.Select(r => r.Text));

        // 最终方案（整张走白字二值化）
        var newBytes = WhiteBinarize(raw, w, 0, 0, w, h, WhiteThreshold);
        var newRes = await engine.RecognizeAsync(newBytes);
        var filtered = newRes.Where(r => r.Confidence >= ConfCutoff).ToList();
        var newText = string.Join("", filtered.Select(r => r.Text));

        sb.AppendLine($"    原图直入  → '{origText}'  [{FormatBlocks(origRes)}]");
        sb.AppendLine($"    最终方案  → '{newText}'  [过滤前 {newRes.Count} block, 过滤后 {filtered.Count}]  [{FormatBlocks(newRes)}]");
        sb.AppendLine();
    }

    private static string FormatBlocks(IEnumerable<OcrResult> res)
        => string.Join(", ", res.Select(r => $"'{r.Text}'@{r.Confidence:F2}"));

    private static byte[] WhiteBinarize(byte[] raw, int fullW, int x, int y, int cw, int ch, int thr)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            int dstOff = row * cw * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = raw[srcOff + col * 4 + 0];
                byte G = raw[srcOff + col * 4 + 1];
                byte R = raw[srcOff + col * 4 + 2];
                byte v = Math.Min(B, Math.Min(G, R)) >= thr ? (byte)0x00 : (byte)0xFF;
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
