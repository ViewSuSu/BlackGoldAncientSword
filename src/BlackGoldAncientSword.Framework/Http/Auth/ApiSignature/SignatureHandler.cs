using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.Http.Auth.ApiSignature
{
    /// <summary>
    /// 每个请求出栈前拿 ticket 并追加 <c>appId/timestamp/nonce/sign</c> 头。
    /// <para>
    /// 一旦服务器判定签名过期（例如返回签名相关错误 code），可调 <see cref="ISignatureTicketProvider.Invalidate"/>
    /// 让下次请求强制刷新——此处不主动做，交由上层重试策略决定。
    /// </para>
    /// </summary>
    public sealed class SignatureHandler : DelegatingHandler
    {
        private readonly ISignatureTicketProvider _ticketProvider;
        private readonly Func<long> _clockMs;
        private readonly Func<string> _nonceGenerator;

        public SignatureHandler(ISignatureTicketProvider ticketProvider)
            : this(ticketProvider, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), RequestSigner.GenerateNonce) { }

        internal SignatureHandler(
            ISignatureTicketProvider ticketProvider,
            Func<long> clockMs,
            Func<string> nonceGenerator)
        {
            _ticketProvider = ticketProvider;
            _clockMs = clockMs;
            _nonceGenerator = nonceGenerator;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 排查"同参数同一时刻并发两次"：记录方法+完整 URI+线程 ID+高精度时间戳，
            // 重复请求会表现为两条 URI 相同、时间戳几乎一致的记录。
            AppLog.Info(
                nameof(SignatureHandler),
                $"HTTP {request.Method} {request.RequestUri} tid={Environment.CurrentManagedThreadId} ts={DateTime.Now:HH:mm:ss.fff}");

            var ticket = await _ticketProvider.GetAsync(cancellationToken).ConfigureAwait(false);
            var timestamp = _clockMs();
            var nonce = _nonceGenerator();
            await RequestSigner.SignAsync(request, ticket, timestamp, nonce, cancellationToken).ConfigureAwait(false);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
