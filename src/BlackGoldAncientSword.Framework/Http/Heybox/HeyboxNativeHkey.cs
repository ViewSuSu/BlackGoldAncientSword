using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public static class HeyboxNativeHkey
    {
        private static readonly byte[] Seed =
            Encoding.UTF8.GetBytes("LFEJGJJOKCEHODNMNFIKKONFBKIHJFKB");

        private const uint ReflectedPoly = 0x88A4B5A0u;

        private const string NonceAlphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        private const int NonceLength = 32;

        public static string Compute(string path, long epochSeconds, string deviceId, string userId)
        {
            RequireNotBlank(path, nameof(path));
            RequireNotBlank(deviceId, nameof(deviceId));
            RequireNotBlank(userId, nameof(userId));

            var time = epochSeconds.ToString(CultureInfo.InvariantCulture);
            var source = NormalizePath(path) + time + deviceId + userId;
            var material = HmacSha512(Encoding.UTF8.GetBytes(source));
            var value = Crc32Reflected(material) ^ uint.MaxValue;
            return value.ToString("X", CultureInfo.InvariantCulture);
        }

        public static string NewNonce()
        {
            var chars = new char[NonceLength];
            for (var index = 0; index < NonceLength; index++)
            {
                chars[index] = NonceAlphabet[RandomNumberGenerator.GetInt32(NonceAlphabet.Length)];
            }

            return new string(chars);
        }

        public static string NewDeviceId(int length = 16)
        {
            const string hex = "0123456789abcdef";
            var chars = new char[length];
            for (var index = 0; index < length; index++)
            {
                chars[index] = hex[RandomNumberGenerator.GetInt32(hex.Length)];
            }

            return new string(chars);
        }

        public static string NewRoundToken()
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            return "14:" + Convert.ToHexString(bytes);
        }

        private static string NormalizePath(string path)
        {
            var normalized = path.StartsWith('/') ? path : "/" + path;
            return normalized.EndsWith('/') ? normalized : normalized + "/";
        }

        private static byte[] HmacSha512(byte[] source)
        {
            using var mac = new HMACSHA512(Seed);
            return mac.ComputeHash(source);
        }

        private static uint Crc32Reflected(byte[] data)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ ReflectedPoly : crc >> 1;
                }
            }

            return crc;
        }

        private static void RequireNotBlank(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"signing {field} must not be blank", field);
            }
        }
    }
}
