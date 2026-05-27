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

                // ʹ�� FileStream(useAsync: true) ʵ���������첽д��
                await using var fileStream = new System.IO.FileStream(
                    filePath, System.IO.FileMode.Create, System.IO.FileAccess.Write,
                    System.IO.FileShare.None, bufferSize: 4096, useAsync: true);
                await stream.CopyToAsync(fileStream);
            }
            catch
            {
                // ����д��ʧ�ܲ�Ӱ��������
            }
        }

        /// <summary>
        /// ��ȡ����Ŀ¼�ܴ�С��.NET 8 ��ԭ���첽Ŀ¼������ʹ�� Task.Run ί�к�̨�̡߳�
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
        /// ��ջ���Ŀ¼��.NET 8 ��ԭ���첽 Delete��ʹ�� Task.Run ί�к�̨�̡߳�
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
                    // ���ʧ�ܲ�Ӱ��������
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
