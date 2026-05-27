using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Core.Extensions
{
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public class UrlToImageSourceConverter : IValueConverter
    {
        private static readonly System.Net.Http.HttpClient _httpClient = new();
        private static IImageCacheService? _cacheService;

        public static void SetCacheService(IImageCacheService cacheService)
        {
            _cacheService = cacheService;
        }

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrEmpty(url))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();

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
                    bitmap.DownloadCompleted += (_, _) =>
                    {
                        if (bitmap.CanFreeze)
                            bitmap.Freeze();

                        CacheImageAsync(capturedUrl).SafeFireAndForget("UrlToImageSourceConverter.CacheImage");
                    };
                }
                else
                {
                    if (bitmap.CanFreeze)
                        bitmap.Freeze();
                }

                return bitmap;
            }
            catch
            {
                return null;
            }
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
