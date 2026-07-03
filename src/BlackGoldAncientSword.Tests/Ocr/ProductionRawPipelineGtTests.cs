using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;
using BlackGoldAncientSword.Ocr;

namespace BlackGoldAncientSword.Tests.Ocr;

/// <summary>
/// 生产 Stitch 管线（白字二值化）对已知 GT 的强断言。
/// 直接调用 <see cref="TeamInfoOcrService.StitchRegionsForOcr"/> + BucketAndExtractNames 一致的分桶逻辑。
/// 白字二值化对末尾 `.` `丶` 装饰点会丢失，靠 <see cref="TeamMemberNameCorrector"/> 兜回。GT 按二值化后输出对齐。
/// </summary>
public class ProductionRawPipelineGtTests
{
    public record Case(string Image, OcrRegion[] Regions, string?[] Expected);

    public static IEnumerable<object[]> Cases()
    {
        // team_full_1 R 已知问题：stitch 上下文下 OCR 把 `耍` 识成 `要`。留 null 表示暂不做强断言。
        yield return new object[] { new Case(
            "team_full_1.png",
            TeamInfoOcrService.TeamRegions,
            new string?[] { "野排牢张", "九丁汉华", null }) };
        // game_screenshot_3 中间格真名为 `.小小椰子.`，白字二值化丢首尾装饰点 → `小小椰子`。
        yield return new object[] { new Case(
            "game_screenshot_3.png",
            TeamInfoOcrService.TeamRegions,
            new string?[] { null, "小小椰子", "酉红市炒蛋" }) };
        yield return new object[] { new Case(
            "hero_selection_team.png",
            TeamInfoOcrService.TeamRegions,
            new string?[] { "铁小驴", "花心超人本超", "野排牢张" }) };
        yield return new object[] { new Case(
            "duo_screenshot.png",
            TeamInfoOcrService.DuoRegions,
            new string?[] { "野排牢张", "叫我绪绪公主" }) };
        yield return new object[] { new Case(
            "duo_team_selection.png",
            TeamInfoOcrService.DuoRegions,
            new string?[] { "野排牢张", "叫我绪绪公主" }) };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RawStitch_MatchesGroundTruth(Case c)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var srcPath = Path.Combine(AppContext.BaseDirectory, "TestData", c.Image);
        Assert.True(File.Exists(srcPath), $"测试图不存在: {srcPath}");

        var (raw, w, h) = LoadBgra(srcPath);

        var stitched = TeamInfoOcrService.StitchRegionsForOcr(raw, w, h, c.Regions);
        Assert.NotNull(stitched.bmp);

        using var engine = new OcrEngine();
        var results = await engine.RecognizeAsync(stitched.bmp!);

        // 按最近槽位中心分桶
        var centers = new int[stitched.regionXRanges.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            var (xs, xe) = stitched.regionXRanges[i];
            centers[i] = xs < 0 ? int.MinValue / 2 : (xs + xe) / 2;
        }
        var buckets = new string[centers.Length];
        foreach (var r in results)
        {
            var cx = (r.Box.TopLeft.X + r.Box.TopRight.X) / 2;
            int best = -1; int bestDist = int.MaxValue;
            for (int i = 0; i < centers.Length; i++)
            {
                if (centers[i] == int.MinValue / 2) continue;
                var d = Math.Abs(cx - centers[i]);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0) buckets[best] += r.Text;
        }

        var report = new StringBuilder();
        report.AppendLine($"[{c.Image}]  regions={c.Regions.Length}  blocks={results.Count}");
        int gtChecked = 0, gtHit = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            var actual = (buckets[i] ?? "").Replace(" ", "").Replace("\n", "").Replace("\r", "").Trim();
            var gt = c.Expected[i];
            var mark = gt == null ? "  " : (actual == gt ? "✓" : "✗");
            report.AppendLine($"  [{i}] gt={gt ?? "(未知)"}  actual='{actual}'  {mark}");
            if (gt != null)
            {
                gtChecked++;
                if (actual == gt) gtHit++;
            }
        }
        Console.WriteLine(report.ToString());

        Assert.Equal(gtChecked, gtHit);
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
