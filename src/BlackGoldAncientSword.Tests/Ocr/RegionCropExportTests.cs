using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using SkiaSharp;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 把各测试用例里用到的图片按现有归一化区域裁剪出来，
/// 存到桌面 OCR_Regions/&lt;image&gt;/ 目录，文件名带 trio/duo + 区域标签。
/// 供人工肉眼审阅每个 region 的裁剪是否合适。
/// </summary>
public class RegionCropExportTests
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

    // game_screenshot_3.png 的扩宽区（NicknamePipelineSweepTests 用）
    private static readonly OcrRegion[] Gs3WidenedRegions = new[]
    {
        new OcrRegion { X = 730 / 2560.0,  Y = 1456 / 1600.0, Width = 364 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1164 / 2560.0, Y = 1456 / 1600.0, Width = 368 / 2560.0, Height = 54 / 1600.0 },
        new OcrRegion { X = 1610 / 2560.0, Y = 1456 / 1600.0, Width = 384 / 2560.0, Height = 54 / 1600.0 },
    };

    // (image, mode, regions, labels)
    private static readonly (string img, string mode, OcrRegion[] regs, string[] labels)[] Jobs = new[]
    {
        ("hero_selection_team.png",     "trio", TrioRegions, new[] { "L", "M", "R" }),
        ("hero_selection_team_02.png",  "trio", TrioRegions, new[] { "L", "M", "R" }),
        ("team_full_1.png",             "trio", TrioRegions, new[] { "L", "M", "R" }),
        ("game_screenshot_3.png",       "trio_widened", Gs3WidenedRegions, new[] { "L", "M", "R" }),
        ("duo_screenshot.png",          "duo",  DuoRegions,  new[] { "L", "R" }),
        ("duo_team_selection.png",      "duo",  DuoRegions,  new[] { "L", "R" }),
    };

    [Fact]
    public void ExportRegionCropsToDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var rootDir = Path.Combine(desktop, "OCR_Regions");
        if (Directory.Exists(rootDir)) Directory.Delete(rootDir, recursive: true);
        Directory.CreateDirectory(rootDir);

        foreach (var (img, mode, regs, labels) in Jobs)
        {
            var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", img);
            if (!File.Exists(srcPath)) continue;

            var (raw, w, h) = LoadBgra(srcPath);
            var imgStem = Path.GetFileNameWithoutExtension(img);
            var subDir = Path.Combine(rootDir, imgStem);
            Directory.CreateDirectory(subDir);

            for (int i = 0; i < regs.Length; i++)
            {
                int cx = (int)(regs[i].X * w);
                int cy = (int)(regs[i].Y * h);
                int cw = (int)(regs[i].Width * w);
                int ch = (int)(regs[i].Height * h);
                cx = Math.Max(0, cx);
                cy = Math.Max(0, cy);
                cw = Math.Min(cw, w - cx);
                ch = Math.Min(ch, h - cy);
                if (cw <= 0 || ch <= 0) continue;

                // 1) 裸色裁剪
                var rawPng = CropRawToPng(raw, w, cx, cy, cw, ch);
                var rawName = $"{mode}_{labels[i]}_{imgStem}_raw.png";
                File.WriteAllBytes(Path.Combine(subDir, rawName), rawPng);

                // 2) 生产管线：白字二值化 (阈值 200)
                var wbBmp = TeamInfoOcrService.CropAndBinarizeWhite(
                    raw, w, h, regs[i]).imageBytes;
                if (wbBmp != null)
                {
                    var wbName = $"{mode}_{labels[i]}_{imgStem}_wb200.bmp";
                    File.WriteAllBytes(Path.Combine(subDir, wbName), wbBmp);
                }
            }
        }

        // 断言：文件夹非空
        var produced = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories);
        Assert.True(produced.Length > 0, "未生成任何裁剪文件");
    }

    private static byte[] CropRawToPng(byte[] raw, int fullW, int x, int y, int cw, int ch)
    {
        var buf = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
            Array.Copy(raw, (y + row) * fullW * 4 + x * 4, buf, row * cw * 4, cw * 4);

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
