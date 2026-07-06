using System;
using System.Security.Cryptography;
using System.Text;

namespace BlackGoldAncientSword.Framework.Http.Auth.Captcha
{
    /// <summary>
    /// 与网页 <c>CryptoJS.AES.encrypt(plain, key, {mode:ECB, padding:PKCS7}).toString()</c> 对齐。
    /// key 的字节数决定 AES-128/192/256（网页里 secretKey 通常是 16 字节 → AES-128）。
    /// 输出是 CryptoJS 默认的 base64（`.toString()`）。
    /// </summary>
    public static class AesEcbCipher
    {
        public static string EncryptToBase64(string plain, string keyUtf8)
        {
            if (plain is null) throw new ArgumentNullException(nameof(plain));
            if (keyUtf8 is null) throw new ArgumentNullException(nameof(keyUtf8));

            var keyBytes = Encoding.UTF8.GetBytes(keyUtf8);
            var plainBytes = Encoding.UTF8.GetBytes(plain);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keyBytes;

            using var enc = aes.CreateEncryptor();
            var cipher = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return Convert.ToBase64String(cipher);
        }
    }
}
