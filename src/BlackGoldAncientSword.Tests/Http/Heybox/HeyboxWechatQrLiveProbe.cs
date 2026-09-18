using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Http.Heybox;
using Xunit;
using Xunit.Abstractions;

namespace BlackGoldAncientSword.Tests.Http.Heybox
{

    [Trait("Category", "Live")]
    [Collection(HeyboxLiveCollection.Name)]
    public class HeyboxWechatQrLiveProbe
    {
        private readonly ITestOutputHelper _output;

        public HeyboxWechatQrLiveProbe(ITestOutputHelper output) => _output = output;

        [Fact]
        public async Task Probe_WechatQrLogin_Native()
        {
            var login = new HeyboxQrLoginService();

            _output.WriteLine("=== ① 取微信二维码 ===");
            var challenge = await login.CreateAsync(CancellationToken.None);
            if (challenge is null)
            {
                _output.WriteLine("!! 取二维码失败（多半是 redirect_uri 未被该 appid 授权，或微信改了页面结构）。");
                return;
            }

            _output.WriteLine($"  uuid = {challenge.Uuid}");
            _output.WriteLine($"  图片大小 = {challenge.ImageBytes.Length:N0} 字节");

            var window = QrWindow.Show(challenge.ImageBytes, "微信扫码登录小黑盒（Live 探针）");
            try
            {
                PrintAscii(challenge.ImageBytes);
                _output.WriteLine("");
                _output.WriteLine("=== ③ 等扫码（最多 3 分钟）===");

                var session = await WaitForScanAsync(login, challenge.Uuid);
                if (session is null) return;

                new DpapiHeyboxSessionStore().Save(session);
                _output.WriteLine("");
                _output.WriteLine($"  OK  heybox_id={session.HeyboxId}，登录态已存到 {DpapiHeyboxSessionStore.DefaultPath}");
                _output.WriteLine("  （pkey 是会话凭证，不打印）");
            }
            finally
            {
                window?.Close();
            }
        }

        private async Task<HeyboxSession?> WaitForScanAsync(IHeyboxQrLoginService login, string uuid)
        {
            var deadline = DateTime.UtcNow.AddMinutes(3);
            var last = (HeyboxQrOutcome?)null;

            while (DateTime.UtcNow < deadline)
            {
                var result = await login.PollAsync(uuid, CancellationToken.None);

                if (result.Outcome != last)
                {
                    _output.WriteLine($"  {result.Outcome}（{Describe(result.Outcome)}）");
                    last = result.Outcome;
                }

                switch (result.Outcome)
                {
                    case HeyboxQrOutcome.Success:
                        return result.Session;
                    case HeyboxQrOutcome.Expired:
                        _output.WriteLine("!! 二维码失效或被取消，重新跑一次即可。");
                        return null;
                }

                await Task.Delay(1500);
            }

            _output.WriteLine("!! 3 分钟内没等到扫码确认。");
            return null;
        }

        private static string Describe(HeyboxQrOutcome outcome) => outcome switch
        {
            HeyboxQrOutcome.WaitingScan => "等待扫码",
            HeyboxQrOutcome.Scanned => "已扫码，等待手机上确认",
            HeyboxQrOutcome.Success => "已确认，登录态已拿到",
            HeyboxQrOutcome.Expired => "二维码失效或被取消",
            _ => "轮询失败，继续等",
        };

        private static void PrintAscii(byte[] imageBytes)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(imageBytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
            gray.Freeze();

            var width = gray.PixelWidth;
            var height = gray.PixelHeight;
            var pixels = new byte[width * height];
            gray.CopyPixels(pixels, width, 0);

            const int targetColumns = 78;
            var scale = Math.Max(1, width / targetColumns);

            var sb = new StringBuilder();
            for (var y = 0; y + 2 * scale <= height; y += 2 * scale)
            {
                for (var x = 0; x + scale <= width; x += scale)
                {
                    var px = pixels[(y + scale) * width + (x + scale / 2)];
                    sb.Append(px < 128 ? '#' : ' ');
                }
                sb.Append('\n');
            }

            using var raw = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            raw.WriteLine();
            raw.WriteLine(sb.ToString());
            raw.Flush();
        }
    }
}
