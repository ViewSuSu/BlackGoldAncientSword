using System;

namespace BlackGoldAncientSword.Framework.Http
{
    /// <summary>
    /// 后端业务响应错误。
    /// 语义：HTTP 已通、后端已返回，但业务层认定失败——由响应体 code != 成功值触发，
    /// 也用于 HTTP 非 2xx 但 body 里携带了 msg 字段的情况。
    /// <para>
    /// <see cref="Msg"/> 直接透传响应体的 msg。若后端未给 msg（如 4xx/5xx 空 body），
    /// 则为 null——上层必须容忍并选择静默，而不是拼英文兜底串。
    /// </para>
    /// </summary>
    public sealed class NarakaApiException : Exception
    {
        /// <summary>响应体 code；HTTP 非 2xx 时为负数（-HttpStatusCode），以便与业务码区分。</summary>
        public int Code { get; }

        /// <summary>响应体 msg 原文；后端未返回时为 null。</summary>
        public string? Msg { get; }

        public NarakaApiException(int code, string? msg)
            : base(msg ?? string.Empty)
        {
            Code = code;
            Msg = msg;
        }
    }
}
