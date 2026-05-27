using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    public interface IImageCacheService
    {
        string CachePath { get; set; }
        string? GetCachedFilePath(string url);
        Task CacheFromStreamAsync(string url, System.IO.Stream stream);
        Task<long> GetCacheSizeBytesAsync();
        Task ClearCacheAsync();
    }
}