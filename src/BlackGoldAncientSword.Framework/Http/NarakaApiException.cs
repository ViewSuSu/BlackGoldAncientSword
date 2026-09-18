using System;

namespace BlackGoldAncientSword.Framework.Http
{

    public sealed class NarakaApiException : Exception
    {
        public int Code { get; }

        public string? Msg { get; }

        public NarakaApiException(int code, string? msg)
            : base(msg ?? string.Empty)
        {
            Code = code;
            Msg = msg;
        }
    }
}
