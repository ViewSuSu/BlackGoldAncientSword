using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Services.Abstractions;

namespace BlackGoldAncientSword.Framework.Http.Auth.Token
{
    /// <summary>
    /// 请求出栈前若 <see cref="IAuthTokenState.Current"/> 有 token 则加 <c>Authorization: Bearer</c>。
    /// 响应回来后若 status=401 或 body <c>code ∈ {"401","1010003001","1_010_003_001"}</c>，
    /// 单飞 refresh；成功则重发一次；失败则清 token 并调 <see cref="IAuthChallengeService.ShowAsync"/>
    /// 阻塞等待用户重登，登录成功后再重发。
    /// </summary>
    public sealed class AuthTokenHandler : DelegatingHandler
    {
        private readonly IAuthTokenState _state;
        private readonly IAuthTokenStore _store;
        private readonly IAuthTokenRefresher _refresher;
        private readonly IAuthChallengeService _challenge;

        private Task<AuthToken?>? _inflightRefresh;
        private readonly object _refreshSync = new();

        public AuthTokenHandler(
            IAuthTokenState state,
            IAuthTokenStore store,
            IAuthTokenRefresher refresher,
            IAuthChallengeService challenge)
        {
            _state = state;
            _store = store;
            _refresher = refresher;
            _challenge = challenge;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AttachBearer(request, _state.Current);

            var response = await base.SendAsync(request, ct).ConfigureAwait(false);
            if (!await IsUnauthorizedAsync(response, ct).ConfigureAwait(false))
                return response;

            // refresh 单飞
            var newToken = await TryRefreshAsync(ct).ConfigureAwait(false);
            if (newToken != null)
            {
                response.Dispose();
                using var retry = await CloneAsync(request, ct).ConfigureAwait(false);
                AttachBearer(retry, newToken);
                return await base.SendAsync(retry, ct).ConfigureAwait(false);
            }

            // refresh 失败：清 token → 弹登录 → 重发
            _state.Set(null);
            _store.Clear();
            var loggedIn = await _challenge.ShowAsync(ct).ConfigureAwait(false);
            if (!loggedIn) return response;

            response.Dispose();
            using var retryAfterLogin = await CloneAsync(request, ct).ConfigureAwait(false);
            AttachBearer(retryAfterLogin, _state.Current);
            return await base.SendAsync(retryAfterLogin, ct).ConfigureAwait(false);
        }

        private static void AttachBearer(HttpRequestMessage request, AuthToken? token)
        {
            if (request.Headers.Contains("Authorization"))
                request.Headers.Remove("Authorization");
            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.AccessToken);
        }

        private static async Task<bool> IsUnauthorizedAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) return true;
            if (response.Content is null) return false;

            // Peek body without breaking downstream reads: buffer & rewind via ByteArrayContent replace
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var newContent = new ByteArrayContent(bytes);
            foreach (var h in response.Content.Headers)
                newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            response.Content = newContent;

            if (bytes.Length == 0) return false;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                if (!doc.RootElement.TryGetProperty("code", out var codeEl)) return false;
                var codeText = codeEl.ValueKind switch
                {
                    JsonValueKind.String => codeEl.GetString() ?? string.Empty,
                    JsonValueKind.Number => codeEl.ToString(),
                    _ => string.Empty,
                };
                return codeText is "401" or "1010003001" or "1_010_003_001";
            }
            catch
            {
                return false;
            }
        }

        private Task<AuthToken?> TryRefreshAsync(CancellationToken ct)
        {
            var current = _state.Current;
            if (current is null || string.IsNullOrEmpty(current.RefreshToken))
                return Task.FromResult<AuthToken?>(null);

            lock (_refreshSync)
            {
                if (_inflightRefresh != null) return _inflightRefresh;

                _inflightRefresh = RunRefreshAsync(current.RefreshToken, ct);
                return _inflightRefresh;
            }
        }

        private async Task<AuthToken?> RunRefreshAsync(string refreshToken, CancellationToken ct)
        {
            try
            {
                var token = await _refresher.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
                if (token != null)
                {
                    _state.Set(token);
                    _store.Save(token);
                }
                return token;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{nameof(AuthTokenHandler)}.{nameof(RunRefreshAsync)}] failed: {ex.Message}");
                return null;
            }
            finally
            {
                lock (_refreshSync) _inflightRefresh = null;
            }
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage src, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(src.Method, src.RequestUri) { Version = src.Version };
            foreach (var h in src.Headers)
                clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
            foreach (var prop in src.Options)
                ((IDictionary<string, object?>)clone.Options)[prop.Key] = prop.Value;

            if (src.Content != null)
            {
                var body = await src.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var content = new ByteArrayContent(body);
                foreach (var h in src.Content.Headers)
                    content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                clone.Content = content;
            }

            return clone;
        }
    }
}
