using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 用 DPAPI CurrentUser scope 加密 token 到
    /// <c>%APPDATA%\BlackGoldAncientSword\auth.dat</c>。同一 Windows 用户下解密，跨用户或不同机不能解。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class DpapiAuthTokenStore : IAuthTokenStore
    {
        private readonly string _filePath;

        public DpapiAuthTokenStore() : this(DefaultFilePath()) { }

        internal DpapiAuthTokenStore(string filePath)
        {
            _filePath = filePath;
        }

        public AuthToken? Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                var cipher = File.ReadAllBytes(_filePath);
                if (cipher.Length == 0) return null;
                var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var dto = JsonSerializer.Deserialize<PersistedDto>(plain);
                if (dto is null || string.IsNullOrEmpty(dto.AccessToken)) return null;
                return new AuthToken(dto.AccessToken, dto.RefreshToken ?? string.Empty, dto.UserJson, dto.ExpiresAtUnixMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(DpapiAuthTokenStore)}.{nameof(Load)}] failed: {ex.Message}");
                return null;
            }
        }

        public void Save(AuthToken token)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var dto = new PersistedDto
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken,
                    UserJson = token.UserJson,
                    ExpiresAtUnixMs = token.ExpiresAtUnixMs,
                };
                var plain = JsonSerializer.SerializeToUtf8Bytes(dto);
                var cipher = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_filePath, cipher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(DpapiAuthTokenStore)}.{nameof(Save)}] failed: {ex.Message}");
            }
        }

        public void Clear()
        {
            try { if (File.Exists(_filePath)) File.Delete(_filePath); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(DpapiAuthTokenStore)}.{nameof(Clear)}] failed: {ex.Message}");
            }
        }

        private static string DefaultFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "BlackGoldAncientSword", "auth.dat");
        }

        private sealed class PersistedDto
        {
            [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = string.Empty;
            [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
            [JsonPropertyName("userJson")] public string? UserJson { get; set; }
            [JsonPropertyName("expiresAtUnixMs")] public long ExpiresAtUnixMs { get; set; }
        }
    }
}
