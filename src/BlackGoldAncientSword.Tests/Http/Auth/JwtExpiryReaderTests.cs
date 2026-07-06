using System;
using System.Text;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class JwtExpiryReaderTests
    {
        [Fact]
        public void ReadExpiresAtUnixMs_ValidJwt_ReturnsMs()
        {
            var payload = new { exp = 1_735_689_600L };
            var jwt = "header." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload)) + ".sig";
            Assert.Equal(1_735_689_600L * 1000, JwtExpiryReader.ReadExpiresAtUnixMs(jwt));
        }

        [Fact]
        public void ReadExpiresAtUnixMs_NoExp_ReturnsZero()
        {
            var payload = new { sub = "u1" };
            var jwt = "h." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload)) + ".s";
            Assert.Equal(0, JwtExpiryReader.ReadExpiresAtUnixMs(jwt));
        }

        [Fact]
        public void ReadExpiresAtUnixMs_NotJwt_ReturnsZero()
        {
            Assert.Equal(0, JwtExpiryReader.ReadExpiresAtUnixMs("random-token"));
            Assert.Equal(0, JwtExpiryReader.ReadExpiresAtUnixMs(""));
            Assert.Equal(0, JwtExpiryReader.ReadExpiresAtUnixMs(null));
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
