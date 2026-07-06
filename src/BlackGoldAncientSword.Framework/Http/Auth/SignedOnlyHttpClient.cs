using System;
using System.Net.Http;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Http.Auth.ApiSignature;

namespace BlackGoldAncientSword.Framework.Http.Auth
{
    /// <summary>
    /// 只挂 <see cref="SignatureHandler"/> 的 HttpClient——不带 Bearer / 不做 401 refresh，
    /// 专供登录期（滑块 / 扫码 / refresh-token）等还没 token 就要发起的请求，
    /// 避免它们撞 <see cref="Token.AuthTokenHandler"/> 递归 401 拦截。
    /// </summary>
    public interface ISignedOnlyHttpClient
    {
        HttpClient Client { get; }
    }

    [Component(ComponentLifetime.Singleton)]
    public sealed class SignedOnlyHttpClient : ISignedOnlyHttpClient
    {
        public HttpClient Client { get; }

        public SignedOnlyHttpClient(ISignatureTicketProvider ticketProvider)
        {
            var handler = new SignatureHandler(ticketProvider) { InnerHandler = new HttpClientHandler() };
            Client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://naraka.drivod.top"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }
    }
}
