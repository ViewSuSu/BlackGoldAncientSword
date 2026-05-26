using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NarakaBladepoint.StatsAssistant.Framework.Core.Attributes;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Framework.Services.Implementation
{
    [Component(ComponentLifetime.Singleton)]
    public class ImageCacheService : IImageCacheService
    {
        private string _cachePath = string.Empty;

        public string CachePath
        {
            get => _cachePath;
            set
            {
                _cachePath = value;
                if (!string.IsNullOrEmpty(_cachePath) && !System.IO.Directory.Exists(_cachePath))
                    System.IO.Directory.CreateDirectory(_cachePath);
            }
        }

        public string? GetCachedFilePath(string url)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_cachePath))
                return null;

            var fileName = GetCacheFileName(url);
            var filePath = System.IO.Path.Combine(_cachePath, fileName);
            return System.IO.File.Exists(filePath) ? filePath : null;
        }

        public async Task CacheFromStreamAsync(string url, System.IO.Stream stream)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(_cachePath) || stream == null)
                return;

            try
            {
                var fileName = GetCacheFileName(url);
                var filePath = System.IO.Path.Combine(_cachePath, fileName);
                var dir = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                await using var fileStream = System.IO.File.Create(filePath);
                await stream.CopyToAsync(fileStream);
            }
            catch
            {
                // Cache write failure is non-critical
            }
        }

        public Task<long> GetCacheSizeBytesAsync()
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_cachePath) || !System.IO.Directory.Exists(_cachePath))
                    return 0L;

                try
                {
                    return System.IO.Directory.GetFiles(_cachePath, "*", System.IO.SearchOption.AllDirectories)
                        .Sum(f => new System.IO.FileInfo(f).Length);
                }
                catch
                {
                    return 0L;
                }
            });
        }

        public Task ClearCacheAsync()
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_cachePath) || !System.IO.Directory.Exists(_cachePath))
                    return;

                try
                {
                    foreach (var file in System.IO.Directory.GetFiles(_cachePath))
                        System.IO.File.Delete(file);
                    foreach (var dir in System.IO.Directory.GetDirectories(_cachePath))
                        System.IO.Directory.Delete(dir, true);
                }
                catch
                {
                    // Clear failure is non-critical
                }
            });
        }

        private static string GetCacheFileName(string url)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(hash).ToLowerInvariant() + ".cache";
        }
    }
}