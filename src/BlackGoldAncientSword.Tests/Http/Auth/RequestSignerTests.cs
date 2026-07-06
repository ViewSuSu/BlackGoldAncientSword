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
    /// 验证 RequestSigner 与网页 <c>D7</c>（SHA256 hex，key 字典序拼接）严格一致。
    /// 参考值全部本地重算：
    /// <c>echo -n "&lt;payload&gt;" | sha256sum</c> 或等价的 .NET SHA256。
    /// </summary>
    public class RequestSignerTests
    {
        private static readonly SignatureTicket Ticket = new("naraka-h5", "test-secret", 9_999_999_999_999);
        private const long FixedTs = 1_735_689_600_000L; // 2025-01-01T00:00:00Z
        private const string FixedNonce = "0123456789abcdef01234567";

        [Fact]
        public void ComputeSha256Hex_KnownVector_MatchesReference()
        {
            // "abc" -> ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
            var hex = RequestSigner.ComputeSha256Hex("abc");
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
        }

        [Fact]
        public void BuildSortedQuery_SortsKeysLexicographically()
        {
            var uri = new Uri("https://x.example/api?b=2&a=1&c=3", UriKind.Absolute);
            var q = RequestSigner.BuildSortedQuery(uri);
            Assert.Equal("a=1&b=2&c=3", q);
        }

        [Fact]
        public void BuildSortedQuery_RelativeUri_Works()
        {
            var uri = new Uri("/app-api/record/search?name=hello", UriKind.Relative);
            var q = RequestSigner.BuildSortedQuery(uri);
            Assert.Equal("name=hello", q);
        }

        [Fact]
        public void BuildSortedQuery_EmptyOrNoQuery_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, RequestSigner.BuildSortedQuery(new Uri("https://x/y", UriKind.Absolute)));
            Assert.Equal(string.Empty, RequestSigner.BuildSortedQuery(new Uri("/x", UriKind.Relative)));
            Assert.Equal(string.Empty, RequestSigner.BuildSortedQuery(null));
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
        public async Task Sign_WritesFourHeaders_WithExpectedValues()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/app-api/record/search?name=abc");
            await RequestSigner.SignAsync(req, Ticket, FixedTs, FixedNonce);

            Assert.Contains(req.Headers, h => h.Key == "appId");
            Assert.Contains(req.Headers, h => h.Key == "timestamp");
            Assert.Contains(req.Headers, h => h.Key == "nonce");
            Assert.Contains(req.Headers, h => h.Key == "sign");

            Assert.Equal("naraka-h5", string.Join(",", req.Headers.GetValues("appId")));
            Assert.Equal("1735689600000", string.Join(",", req.Headers.GetValues("timestamp")));
            Assert.Equal(FixedNonce, string.Join(",", req.Headers.GetValues("nonce")));

            // 复算：payload = sortedQuery + rawBody + sortedSignHeaders + appSecret
            //         = "name=abc" + "" + "appId=naraka-h5&nonce=<nonce>&timestamp=<ts>" + "test-secret"
            var expectedPayload = "name=abc"
                + string.Empty
                + $"appId=naraka-h5&nonce={FixedNonce}&timestamp={FixedTs}"
                + "test-secret";
            var expectedSign = RequestSigner.ComputeSha256Hex(expectedPayload);
            Assert.Equal(expectedSign, string.Join(",", req.Headers.GetValues("sign")));
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
    }
}
