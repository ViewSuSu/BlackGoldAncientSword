using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Cache;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public class UrlToImageSourceConverter : IValueConverter
    {
        private static readonly System.Net.Http.HttpClient _httpClient = new();
        private static IImageCacheService? _cacheService;

        /// <summary>
        /// BitmapImage 对象缓存：以 URL 为键缓存已冻结的 BitmapImage。
        /// 确保相同 URL 不会反复创建新的 BitmapImage 实例，从而避免 WPF 非托管
        /// milcore/WIC 解码内存持续增长。
        /// 实际运行时不同 URL 数量有限（头像 + 段位图标 ≈ 10~30 个），不会导致内存无限增长。
        /// </summary>
        private static readonly ConcurrentDictionary<string, BitmapImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 清理图片缓存，立即释放对 BitmapImage 的引用，使 WPF 非托管解码内存可被回收。
        /// </summary>
        public static void ClearCache() => _imageCache.Clear();

        public static void SetCacheService(IImageCacheService cacheService)
        {
            _cacheService = cacheService;
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrEmpty(url))
                return null;

            // 解码目标宽度：显示尺寸很小（列表头像 30px、头像 52px、段位 100px），
            // 按显示尺寸解码而非原图全尺寸，可把 WIC 解码成本降低一个数量级——
            // 这是战绩页一次绑定 30~50 张头像时"一瞬卡顿"的主因（OnLoad 在绑定线程同步全尺寸解码）。
            // 参数可选，不传按 0（不缩放）；缓存键带上宽度，避免不同尺寸互相覆盖。
            var decodeWidth = ParseDecodeWidth(parameter);
            var cacheKey = decodeWidth > 0 ? url + "|w" + decodeWidth : url;

            // 先查缓存：如果已有相同 URL 的冻结 BitmapImage，直接复用
            if (_imageCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                if (decodeWidth > 0)
                    bitmap.DecodePixelWidth = decodeWidth;

                // 启用 WPF 内置 HTTP 缓存，避免相同 URL 的重复网络请求
                bitmap.UriCachePolicy = new RequestCachePolicy(RequestCacheLevel.Default);

                var cacheFile = _cacheService?.GetCachedFilePath(url);
                if (cacheFile != null)
                {
                    bitmap.UriSource = new Uri(cacheFile, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                }
                else
                {
                    bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.Default;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                }

                bitmap.EndInit();

                if (cacheFile == null && bitmap.IsDownloading)
                {
                    var capturedUrl = url;
                    var capturedKey = cacheKey;
                    bitmap.DownloadCompleted += Handler;
                    void Handler(object? sender, EventArgs e)
                    {
                        bitmap.DownloadCompleted -= Handler;
                        if (bitmap.CanFreeze)
                            bitmap.Freeze();

                        CacheImageAsync(capturedUrl).SafeFireAndForget("UrlToImageSourceConverter.CacheImage");

                        // 下载完成后将冻结的 BitmapImage 加入缓存，供后续复用
                        _imageCache.TryAdd(capturedKey, bitmap);
                    }
                }
                else
                {
                    if (bitmap.CanFreeze)
                        bitmap.Freeze();

                    // 从本地缓存读取时直接缓存冻结后的 BitmapImage
                    _imageCache.TryAdd(cacheKey, bitmap);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, nameof(UrlToImageSourceConverter), $"Convert failed for '{url}'");
                return null;
            }
        }

        /// <summary>
        /// 从缓存中移除指定 URL 对应的 BitmapImage（含所有解码宽度变体，键形如 "url" 或 "url|wNN"）。
        /// </summary>
        public static void InvalidateCacheEntry(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            foreach (var key in _imageCache.Keys)
            {
                if (key == url || key.StartsWith(url + "|w", StringComparison.OrdinalIgnoreCase))
                    _imageCache.TryRemove(key, out _);
            }
        }

        /// <summary>解析 ConverterParameter 为解码目标宽度（像素）；无法解析或 ≤0 返回 0（不缩放）。</summary>
        private static int ParseDecodeWidth(object? parameter)
        {
            if (parameter == null) return 0;
            var s = parameter as string ?? parameter.ToString();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0 ? w : 0;
        }

        /// <summary>
        /// 异步下载图片并写入缓存。使用共享 HttpClient 实例避免 socket 耗尽。
        /// </summary>
        private static async Task CacheImageAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await (_cacheService?.CacheFromStreamAsync(url, stream) ?? Task.CompletedTask);
                }
            }
            catch { }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
