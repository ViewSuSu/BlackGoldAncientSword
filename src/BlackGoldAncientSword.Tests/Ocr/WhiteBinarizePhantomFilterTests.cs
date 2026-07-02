using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 白字二值化在 hero_selection_team_02.png M region 引入幻影 `-`。
/// 尝试三条对策：
///   V1 提高阈值 220/230/240
///   V2 形态学腐蚀 1 像素（kill 细线）
///   V3 识别后按 box 长宽比 + 置信度过滤
/// </summary>
public class WhiteBinarizePhantomFilterTests
{
    private static readonly OcrRegion[] TrioRegions = new[]
    {
        new OcrRegion { X = 0.300781, Y = 0.910000, Width = 0.110938, Height = 0.033750 },
        new OcrRegion { X = 0.470313, Y = 0.910000, Width = 0.112500, Height = 0.033750 },
        new OcrRegion { X = 0.644531, Y = 0.910000, Width = 0.118750, Height = 0.033750 },
    };

    [Fact]
    public async Task Phantom_Elimination_Strategies()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var samples = new[]
        {
            ("hero_selection_team_02.png", 1, "野排牢张", "M（含 `-` 幻影）"),
            ("team_full_1.png",            2, "日常耍废", "R（原有幻影已消，回归检查）"),
            ("hero_selection_team.png",    0, "铁小驴",   "L（原有幻影已消，回归检查）"),
            ("team_full_1.png",            0, "野排牢张", "L 正常样本"),
            ("team_full_1.png",            1, "九丁汉华", "M 正常样本"),
        };

        using var engine = new OcrEngine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outDir = Path.Combine(desktop, "phantom_filter");
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();

        foreach (var (fileName, regionIdx, expected, note) in samples)
        {
            var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
            var (raw, w, h) = LoadBgra(srcPath);
            var region = TrioRegions[regionIdx];
            int cx = (int)(region.X * w), cy = (int)(region.Y * h);
            int cw = (int)(region.Width * w), ch = (int)(region.Height * h);

            sb.AppendLine($"━━━ {fileName}  region idx={regionIdx}  {note}  期望='{expected}' ━━━");

            // 基线 P1: 白二值化 阈值 200
            await Run(engine, WhiteBin(raw, w, cx, cy, cw, ch, 200, erode: 0), expected, "P1 白200", sb);

            // V1 提阈值
            await Run(engine, WhiteBin(raw, w, cx, cy, cw, ch, 220, erode: 0), expected, "V1a 白220", sb);
            await Run(engine, WhiteBin(raw, w, cx, cy, cw, ch, 230, erode: 0), expected, "V1b 白230", sb);
            await Run(engine, WhiteBin(raw, w, cx, cy, cw, ch, 240, erode: 0), expected, "V1c 白240", sb);

            // V2 腐蚀 1px（先白二值化再腐蚀 kill 细线）
            var v2 = WhiteBin(raw, w, cx, cy, cw, ch, 200, erode: 1);
            File.WriteAllBytes(Path.Combine(outDir, $"{fileName}_{regionIdx}_V2erode1.png"), v2);
            await Run(engine, v2, expected, "V2 白200+腐蚀1", sb);

            // V3 识别后过滤：长宽比 > 3 且 conf < 0.7 视为幻影
            var p1Bytes = WhiteBin(raw, w, cx, cy, cw, ch, 200, erode: 0);
            var p1Res = await engine.RecognizeAsync(p1Bytes);
            var filtered = p1Res.Where(r =>
            {
                double bw = r.Box.TopRight.X - r.Box.TopLeft.X;
                double bh = r.Box.BottomLeft.Y - r.Box.TopLeft.Y;
                if (bh <= 0) return true;
                double aspect = bw / bh;
                bool likelyPhantom = aspect > 3.0 && r.Confidence < 0.7;
                return !likelyPhantom;
            }).ToList();
            var filteredText = string.Join("", filtered.Select(r => r.Text));
            var ok = filteredText == expected ? "✅" : (filteredText.Contains(expected) ? "◐" : "❌");
            sb.AppendLine($"  [V3 白200+aspect过滤] {ok}  '{filteredText}'  ({filtered.Count}/{p1Res.Count} block)");
            foreach (var r in p1Res)
            {
                double bw = r.Box.TopRight.X - r.Box.TopLeft.X;
                double bh = r.Box.BottomLeft.Y - r.Box.TopLeft.Y;
                double aspect = bh > 0 ? bw / bh : 0;
                sb.AppendLine($"      · '{r.Text}' conf={r.Confidence:F3}  box={bw:F0}x{bh:F0}  aspect={aspect:F2}");
            }

            sb.AppendLine();
        }

        Console.WriteLine(sb.ToString());
        File.WriteAllText(Path.Combine(outDir, "_summary.txt"), sb.ToString(), Encoding.UTF8);
    }

    private static async Task Run(OcrEngine engine, byte[] img, string expected, string tag, StringBuilder sb)
    {
        var results = await engine.RecognizeAsync(img);
        var joined = string.Join("", results.Select(r => r.Text));
        var ok = joined == expected ? "✅" : (joined.Contains(expected) ? "◐" : "❌");
        sb.Append($"  [{tag,-18}] {ok}  '{joined}'  ({results.Count} block)  ");
        if (results.Count > 0)
            sb.Append($"conf=[{string.Join(",", results.Select(r => r.Confidence.ToString("F2")))}]");
        sb.AppendLine();
    }

    /// <summary>白字二值化 + 反色 + 可选腐蚀。</summary>
    private static byte[] WhiteBin(byte[] raw, int fullW, int x, int y, int cw, int ch, int thr, int erode)
    {
        // 步骤 1: 二值 mask（1=白字，0=背景）
        var mask = new byte[cw * ch];
        for (int row = 0; row < ch; row++)
        {
            int srcOff = (y + row) * fullW * 4 + x * 4;
            for (int col = 0; col < cw; col++)
            {
                byte B = raw[srcOff + col * 4 + 0];
                byte G = raw[srcOff + col * 4 + 1];
                byte R = raw[srcOff + col * 4 + 2];
                mask[row * cw + col] = Math.Min(B, Math.Min(G, R)) >= thr ? (byte)1 : (byte)0;
            }
        }

        // 步骤 2: 腐蚀（4-邻域，erode 轮）
        for (int e = 0; e < erode; e++)
        {
            var next = new byte[mask.Length];
            for (int row = 1; row < ch - 1; row++)
                for (int col = 1; col < cw - 1; col++)
                {
                    int idx = row * cw + col;
                    if (mask[idx] == 1 &&
                        mask[idx - 1] == 1 && mask[idx + 1] == 1 &&
                        mask[idx - cw] == 1 && mask[idx + cw] == 1)
                        next[idx] = 1;
                }
            mask = next;
        }

        // 步骤 3: mask → 反色 BGRA (1→黑 0x00, 0→白 0xFF)
        var buf = new byte[cw * ch * 4];
        for (int i = 0; i < mask.Length; i++)
        {
            byte v = mask[i] == 1 ? (byte)0x00 : (byte)0xFF;
            int off = i * 4;
            buf[off + 0] = v; buf[off + 1] = v; buf[off + 2] = v; buf[off + 3] = 0xFF;
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
