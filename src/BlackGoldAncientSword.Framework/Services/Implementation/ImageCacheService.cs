using System.Security.Cryptography;
using System.Text;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Services.Implementation
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

                // 使用 FileStream(useAsync: true) 实现真正的异步写入
                await using var fileStream = new System.IO.FileStream(
                    filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write,
                    System.IO.FileShare.None, bufferSize: 4096, useAsync: true);
                await stream.CopyToAsync(fileStream);
            }
            catch
            {
                // 缓存写入失败不影响主流程
            }
        }

        /// <summary>
        // 获取缓存目录总大小。.NET 8 无原生异步目录枚举，使用 Task.Run 委托后台线程。
        /// </summary>
        public Task<long> GetCacheSizeBytesAsync()
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_cachePath) || !System.IO.Directory.Exists(_cachePath))
                    return 0L;

                try
                {
                    return System.IO.Directory.EnumerateFiles(_cachePath, "*", System.IO.SearchOption.AllDirectories)
                        .Sum(f => new System.IO.FileInfo(f).Length);
                }
                catch
                {
                    return 0L;
                }
            });
        }

        /// <summary>
        // 清空缓存目录。.NET 8 无原生异步 Delete，使用 Task.Run 委托后台线程。
        /// </summary>
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
                    // 清空失败不影响主流程
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
