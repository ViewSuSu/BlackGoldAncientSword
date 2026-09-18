using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class DpapiHeyboxSessionStore : IHeyboxSessionStore
    {
        public static string DefaultPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlackGoldAncientSword",
            "heybox-session.dat");

        private readonly string _path;

        public DpapiHeyboxSessionStore() : this(DefaultPath) { }

        public DpapiHeyboxSessionStore(string path) => _path = path;

        public HeyboxSession? Load()
        {
            if (!File.Exists(_path)) return null;

            try
            {
                var cipher = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var session = JsonSerializer.Deserialize<HeyboxSession>(plain);
                return string.IsNullOrEmpty(session?.HeyboxId) || string.IsNullOrEmpty(session.Pkey)
                    ? null
                    : session;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                return null;
            }
        }

        public void Save(HeyboxSession session)
        {
            ArgumentNullException.ThrowIfNull(session);

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var plain = JsonSerializer.SerializeToUtf8Bytes(session);
            var cipher = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path, cipher);
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
