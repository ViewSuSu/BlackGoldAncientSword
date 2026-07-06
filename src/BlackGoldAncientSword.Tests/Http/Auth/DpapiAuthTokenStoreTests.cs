using System;
using System.IO;
using BlackGoldAncientSword.Framework.Http.Auth.Token;
using Xunit;

namespace BlackGoldAncientSword.Tests.Http.Auth
{
    /// <summary>
    /// DPAPI 只在 Windows 用户会话内工作。项目 TargetFramework=net10.0-windows 已限定平台。
    /// </summary>
    public class DpapiAuthTokenStoreTests : IDisposable
    {
        private readonly string _tempPath;

        public DpapiAuthTokenStoreTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "bga-tests", Guid.NewGuid().ToString("N") + ".dat");
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new DpapiAuthTokenStore(_tempPath);
            var t = new AuthToken("access-abc", "refresh-def", "{\"n\":1}", 1_234_567_890_000L);
            store.Save(t);
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal(t, loaded);
        }

        [Fact]
        public void Clear_RemovesFile()
        {
            var store = new DpapiAuthTokenStore(_tempPath);
            store.Save(new AuthToken("x", "y", null, 1));
            Assert.True(File.Exists(_tempPath));
            store.Clear();
            Assert.False(File.Exists(_tempPath));
        }

        [Fact]
        public void Load_MissingFile_ReturnsNull()
        {
            var store = new DpapiAuthTokenStore(_tempPath);
            Assert.Null(store.Load());
        }

        public void Dispose()
        {
            try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
        }
    }
}
