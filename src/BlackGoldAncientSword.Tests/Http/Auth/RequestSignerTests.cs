using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    /// <summary>
    /// 验证 <see cref="RequestSigner"/> 与网页 <c>P7</c> 函数严格一致。
    /// 黄金值全部由 Node 直接跑 P7 拷贝出来（见 scripts/p7-golden.mjs 或本项目 doc），
    /// 复算依据：sha256Hex 小写、URL query 未 encode。
    /// </summary>
    public class RequestSignerTests
    {
        private static readonly SignatureTicket Ticket = new("naraka-desktop", "test-secret", 9_999_999_999_999);
        private const long FixedTs = 1_735_689_600_000L; // 2025-01-01T00:00:00Z, str len = 13
        private const string FixedNonce = "0123456789abcdef01234567"; // 24 hex

        [Fact]
        public void ComputeSha256Hex_KnownVector_MatchesReference()
        {
            // "abc" -> ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
            var hex = RequestSigner.ComputeSha256Hex("abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
        }

        [Fact]
        public void MergeParams_SortsAndDecodesValues()
        {
            var uri = new Uri("https://x.example/api?b=2&a=1&c=hello%20world", UriKind.Absolute);
            var merged = RequestSigner.MergeParams(uri);
            Assert.Equal(3, merged.Count);
            Assert.Equal("1", merged["a"]);
            Assert.Equal("2", merged["b"]);
            Assert.Equal("hello world", merged["c"]);
            Assert.Equal("a=1&b=2&c=hello world", RequestSigner.BuildSortedKv(merged));
        }

        [Fact]
        public void MergeParams_RelativeUri_Works()
        {
            var uri = new Uri("/app-api/record/search?name=hello", UriKind.Relative);
            var merged = RequestSigner.MergeParams(uri);
            Assert.Equal("name=hello", RequestSigner.BuildSortedKv(merged));
        }

        [Fact]
        public void MergeParams_EmptyOrNoQuery_ReturnsEmpty()
        {
            Assert.Empty(RequestSigner.MergeParams(new Uri("https://x/y", UriKind.Absolute)));
            Assert.Empty(RequestSigner.MergeParams(new Uri("/x", UriKind.Relative)));
            Assert.Empty(RequestSigner.MergeParams(null));
        }

        [Fact]
        public async Task ReadRawBody_JsonString_ReturnsAsIs()
        {
            using var content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/x") { Content = content };
            var body = await RequestSigner.ReadRawBodyAsync(req, CancellationToken.None);
            Assert.Equal("{\"a\":1}", body);
        }

        [Fact]
        public async Task ReadRawBody_Null_ReturnsEmpty()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/x");
            var body = await RequestSigner.ReadRawBodyAsync(req, CancellationToken.None);
            Assert.Equal(string.Empty, body);
        }

        [Fact]
        public async Task Sign_GetWithQuery_MatchesWebP7Golden()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/app-api/record/search?name=abc");
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);

            AssertHeaderScaffolding(req);
            Assert.Equal("71cb1885c9751e3c7bbb7ca00eea23fa5bb7cdbc672b8d2a0d26e5897bff06f3",
                string.Join(",", req.Headers.GetValues("sign")));
        }

        [Fact]
        public async Task Sign_PostWithQueryAndJsonBody_MatchesWebP7Golden()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/x?b=2&a=1")
            {
                Content = new StringContent("{\"k\":\"v\"}", Encoding.UTF8, "application/json"),
            };
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);

            AssertHeaderScaffolding(req);
            Assert.Equal("08c5a069a97bce0717e1653f3cd7cef47952112977bcbe5539b846bf40e73794",
                string.Join(",", req.Headers.GetValues("sign")));
        }

        [Fact]
        public async Task Sign_PostNoQueryNoBody_MatchesWebP7Golden()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/x");
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);

            AssertHeaderScaffolding(req);
            Assert.Equal("9e9e6a89b964145358235dc227540eef3144bf5f11291982e8a8f5fbc3fabbbc",
                string.Join(",", req.Headers.GetValues("sign")));
        }

        [Fact]
        public async Task Sign_GetNoQueryNoBody_MatchesWebP7Golden()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/y");
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);

            AssertHeaderScaffolding(req);
            Assert.Equal("6585c417cff5c73520d83d1921c4f7ed4e449069bf51432dc15a43bed8d0ec38",
                string.Join(",", req.Headers.GetValues("sign")));
        }

        [Fact]
        public async Task Sign_TwiceOnSameRequest_ReplacesHeadersWithoutDuplicates()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/x?a=1");
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);
            await RequestSigner.SignAsync(req, Ticket, FixedTs + 1, "ffffffffffffffffffffffff");

            Assert.Single(req.Headers.GetValues("appId"));
            Assert.Single(req.Headers.GetValues("sign"));
            Assert.Equal("ffffffffffffffffffffffff", string.Join(",", req.Headers.GetValues("nonce")));
        }

        [Fact]
        public void GenerateNonce_Produces24HexChars()
        {
            var n = RequestSigner.GenerateNonce();
            Assert.Equal(24, n.Length);
            Assert.Matches("^[0-9a-f]{24}$", n);
        }

        private static void AssertHeaderScaffolding(HttpRequestMessage req)
        {
            Assert.Contains(req.Headers, h => h.Key == "appId");
            Assert.Contains(req.Headers, h => h.Key == "timestamp");
            Assert.Contains(req.Headers, h => h.Key == "nonce");
            Assert.Contains(req.Headers, h => h.Key == "sign");

            Assert.Equal("naraka-desktop", string.Join(",", req.Headers.GetValues("appId")));
            Assert.Equal("1735689600000", string.Join(",", req.Headers.GetValues("timestamp")));
            Assert.Equal(FixedNonce, string.Join(",", req.Headers.GetValues("nonce")));
        }
    }
}
