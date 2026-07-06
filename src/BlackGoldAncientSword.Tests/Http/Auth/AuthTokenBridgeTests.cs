using System.Text.Json;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    public class AuthTokenBridgeTests
    {
        [Fact]
        public void TryParse_Valid_ReturnsToken()
        {
            var expiredJwt = "h.e30.s"; // {} payload → no exp
            var json = JsonSerializer.Serialize(new { t = expiredJwt, rt = "rt-value", u = "{\"id\":1}" });
            var token = AuthTokenBridge.TryParse(json);
            Assert.NotNull(token);
            Assert.Equal(expiredJwt, token!.AccessToken);
            Assert.Equal("rt-value", token.RefreshToken);
            Assert.Equal("{\"id\":1}", token.UserJson);
        }

        [Fact]
        public void TryParse_MissingAccess_ReturnsNull()
        {
            var json = JsonSerializer.Serialize(new { rt = "rt-only" });
            Assert.Null(AuthTokenBridge.TryParse(json));
        }

        [Fact]
        public void TryParse_Empty_ReturnsNull()
        {
            Assert.Null(AuthTokenBridge.TryParse(""));
            Assert.Null(AuthTokenBridge.TryParse(null));
        }

        [Fact]
        public void TryParse_Garbage_ReturnsNull()
        {
            Assert.Null(AuthTokenBridge.TryParse("not-json"));
        }
    }
}
