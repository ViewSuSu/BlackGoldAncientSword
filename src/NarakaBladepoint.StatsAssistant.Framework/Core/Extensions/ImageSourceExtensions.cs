using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NarakaBladepoint.StatsAssistant.Framework.Core.Extensions
{
    public static class ImageSourceExtensions
    {
        public static BitmapImage? LoadFromResource(Uri uri)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static string GetFileName(this ImageSource? image)
        {
            if (image is BitmapImage bitmap && bitmap.UriSource != null)
                return System.IO.Path.GetFileNameWithoutExtension(bitmap.UriSource.ToString());
            return string.Empty;
        }

        public static string GetFileNameWithExtension(this ImageSource? image)
        {
            if (image is BitmapImage bitmap && bitmap.UriSource != null)
                return System.IO.Path.GetFileName(bitmap.UriSource.ToString());
            return string.Empty;
        }
    }
}
