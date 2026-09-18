using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{
    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxBrowserLoginService : IHeyboxBrowserLoginService
    {
        private const string LoginBase = "https://login.xiaoheihe.cn";

        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

        private static readonly string CallbackHost = "127.0.0.1";

        public async Task<HeyboxSession?> LoginAsync(CancellationToken ct = default)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var callbackUrl = $"http://{CallbackHost}:{port}/callback";

            try
            {
                var loginUrl = $"{LoginBase}/?origin=heybox&mode=cli" +
                               $"&state={Uri.EscapeDataString(NewState())}" +
                               $"&redirect_url={Uri.EscapeDataString(callbackUrl)}";

                AppLog.Info($"{nameof(HeyboxBrowserLoginService)}.{nameof(LoginAsync)}",
                    $"opening browser for heybox cli login, callback on port {port}");

                try
                {
                    Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"{nameof(HeyboxBrowserLoginService)}.{nameof(LoginAsync)}", "打开浏览器失败");
                    return null;
                }

                var query = await WaitForCallbackAsync(listener, ct).ConfigureAwait(false);
                if (query is null)
                {
                    AppLog.Warning($"{nameof(HeyboxBrowserLoginService)}.{nameof(LoginAsync)}",
                        "no callback within timeout, or cancelled");
                    return null;
                }

                if (query.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
                {
                    AppLog.Warning($"{nameof(HeyboxBrowserLoginService)}.{nameof(LoginAsync)}",
                        $"login page returned error={error}");
                    return null;
                }

                var heyboxId = FirstNonEmpty(query.GetValueOrDefault("heybox_id"), query.GetValueOrDefault("user_heybox_id"));
                var pkey = FirstNonEmpty(query.GetValueOrDefault("pkey"), query.GetValueOrDefault("user_pkey"));

                if (string.IsNullOrEmpty(heyboxId) || string.IsNullOrEmpty(pkey))
                {
                    AppLog.Warning($"{nameof(HeyboxBrowserLoginService)}.{nameof(LoginAsync)}",
                        "callback carried no heybox_id/pkey");
                    return null;
                }

                _ = long.TryParse(FirstNonEmpty(query.GetValueOrDefault("expire_at"), query.GetValueOrDefault("expireAt")),
                    out var expireAt);

                return new HeyboxSession(heyboxId, pkey, expireAt);
            }
            finally
            {
                try { listener.Stop(); } catch (SocketException) {  }
            }
        }

        private static async Task<Dictionary<string, string>?> WaitForCallbackAsync(TcpListener listener, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(LoginTimeout);
            var token = timeoutCts.Token;

            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return null; }
                catch (ObjectDisposedException) { return null; }

                using (client)
                {
                    var stream = client.GetStream();
                    var buffer = new byte[8192];
                    var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    var requestLine = Encoding.ASCII.GetString(buffer, 0, Math.Max(read, 0)).Split("\r\n")[0];

                    var path = requestLine.Split(' ') is { Length: >= 2 } parts ? parts[1] : "/";
                    var isCallback = path.StartsWith("/callback", StringComparison.OrdinalIgnoreCase);

                    var html = isCallback
                        ? "<html><meta charset=\"utf-8\"><body style=\"font-family:sans-serif\">登录态已收到，可以关闭此页回到黑金古剑。</body></html>"
                        : "<html><meta charset=\"utf-8\"><body>waiting</body></html>";
                    var body = Encoding.UTF8.GetBytes(html);
                    var head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                        $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, token).ConfigureAwait(false);
                    await stream.WriteAsync(body, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);

                    if (isCallback) return ParseQuery(path);
                }
            }

            return null;
        }

        private static string NewState()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static Dictionary<string, string> ParseQuery(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idx = path.IndexOf('?');
            if (idx < 0) return result;

            foreach (var pair in path[(idx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }

            return result;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrEmpty(v)) return v;
            return null;
        }
    }
}
