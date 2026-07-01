using SkiaSharp;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 把 TeamInfo 三个 OCR region 的裁剪框以绿色矩形叠加到 hero_selection_team 图上，
/// 导出到桌面用于肉眼验证 region 归一化坐标是否覆盖了昵称完整范围（包含前后缀标点）。
/// </summary>
public class RegionVisualizationTests
{
    private static readonly OcrRegion[] TeamRegions = new[]
    {
        new OcrRegion { X = 0.301953, Y = 0.899306, Width = 0.123661, Height = 0.039583 },
        new OcrRegion { X = 0.475000, Y = 0.897222, Width = 0.125447, Height = 0.041667 },
        new OcrRegion { X = 0.646484, Y = 0.897917, Width = 0.138672, Height = 0.036806 },
    };

    public static IEnumerable<object[]> HeroImages => new[]
    {
        new object[] { "hero_selection_team.png" },
        new object[] { "hero_selection_team_02.png" },
    };

    [Theory]
    [MemberData(nameof(HeroImages))]
    public void DrawTeamRegions_OverlayGreenBoxes_SaveToDesktop(string fileName)
    {
        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        Assert.True(File.Exists(srcPath), $"源图不存在: {srcPath}");

        using var srcBmp = SKBitmap.Decode(srcPath);
        Assert.NotNull(srcBmp);

        int w = srcBmp.Width;
        int h = srcBmp.Height;

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(srcBmp, 0, 0);

        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0, 255, 0, 255),
            StrokeWidth = 3,
            IsAntialias = true,
        };

        using var labelFont = new SKFont
        {
            Size = 20,
        };
        using var labelPaint = new SKPaint
        {
            Color = new SKColor(0, 255, 0, 255),
            IsAntialias = true,
        };

        var labels = new[] { "L", "M", "R" };
        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var r = TeamRegions[i];
            int x = (int)(r.X * w);
            int y = (int)(r.Y * h);
            int rw = (int)(r.Width * w);
            int rh = (int)(r.Height * h);

            var rect = new SKRect(x, y, x + rw, y + rh);
            canvas.DrawRect(rect, stroke);
            canvas.DrawText($"{labels[i]} {rw}x{rh}", x, y - 4, SKTextAlign.Left, labelFont, labelPaint);
        }

        canvas.Flush();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var outName = Path.GetFileNameWithoutExtension(fileName) + "_regions.png";
        var outPath = Path.Combine(desktop, outName);

        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.OpenWrite(outPath))
        {
            data.SaveTo(fs);
        }

        Console.WriteLine($"图片尺寸: {w}x{h}");
        for (int i = 0; i < TeamRegions.Length; i++)
        {
            var r = TeamRegions[i];
            int x = (int)(r.X * w);
            int y = (int)(r.Y * h);
            int rw = (int)(r.Width * w);
            int rh = (int)(r.Height * h);
            Console.WriteLine($"  {labels[i]}: X={x} Y={y} W={rw} H={rh}");
        }
        Console.WriteLine($"输出: {outPath}");

        Assert.True(File.Exists(outPath));
    }
}
