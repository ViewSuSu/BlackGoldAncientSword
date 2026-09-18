using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    public static class HeyboxHkey
    {
        private const string Alphabet = "AB45STUVWZEFGJ6CH01D237IXYPQRKLMN89";

        public static string Compute(string pathOrUrl, long unixSeconds, string nonce)
        {
            var path = ToPath(pathOrUrl);

            var r = MapWithSlicedAlphabet(unixSeconds.ToString(), -2);
            var a = MapWithFullAlphabet(path);
            var o = MapWithFullAlphabet(nonce);

            var s = Interleave(new[] { r, a, o });
            s = s[..Math.Min(20, s.Length)];

            var c = Md5Hex(s);

            var tail = c[^6..].Select(ch => (int)ch).ToArray();
            var l = (Mutate(tail).Sum() % 100).ToString();
            if (l.Length < 2) l = "0" + l;

            var u = MapWithSlicedAlphabet(c[..5], -4);
            return u + l;
        }

        public static string NewNonce()
        {
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes);
        }

        private static string ToPath(string pathOrUrl)
        {
            var s = pathOrUrl;
            var scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                var start = s.IndexOf('/', scheme + 3);
                s = start >= 0 ? s[start..] : "/";
            }

            var q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) s = s[..q];

            var segments = s.Split('/').Where(x => x.Length > 0);
            return "/" + string.Join("/", segments) + "/";
        }

        private static string MapWithSlicedAlphabet(string input, int n)
        {
            var table = n < 0 ? Alphabet[..(Alphabet.Length + n)] : Alphabet[..n];
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input) sb.Append(table[ch % table.Length]);
            return sb.ToString();
        }

        private static string MapWithFullAlphabet(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input) sb.Append(Alphabet[ch % Alphabet.Length]);
            return sb.ToString();
        }

        private static string Interleave(string[] parts)
        {
            var max = parts.Max(p => p.Length);
            var sb = new StringBuilder();
            for (var i = 0; i < max; i++)
            {
                foreach (var p in parts)
                {
                    if (i < p.Length) sb.Append(p[i]);
                }
            }

            return sb.ToString();
        }

        private static int Shift(int x) => (x & 128) != 0 ? ((x << 1) ^ 27) & 0xFF : x << 1;
        private static int N(int x) => Shift(x) ^ x;
        private static int U(int x) => N(Shift(x));
        private static int V(int x) => U(N(Shift(x)));
        private static int W(int x) => V(x) ^ U(x) ^ N(x);

        private static int[] Mutate(int[] t)
        {
            var e = new int[4];
            e[0] = W(t[0]) ^ V(t[1]) ^ U(t[2]) ^ N(t[3]);
            e[1] = N(t[0]) ^ W(t[1]) ^ V(t[2]) ^ U(t[3]);
            e[2] = U(t[0]) ^ N(t[1]) ^ W(t[2]) ^ V(t[3]);
            e[3] = V(t[0]) ^ U(t[1]) ^ N(t[2]) ^ W(t[3]);

            t[0] = e[0];
            t[1] = e[1];
            t[2] = e[2];
            t[3] = e[3];
            return t;
        }

        private static string Md5Hex(string input)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
