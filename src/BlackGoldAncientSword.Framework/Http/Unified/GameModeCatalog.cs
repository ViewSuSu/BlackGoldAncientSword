using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;

namespace BlackGoldAncientSword.Framework.Http.Unified
{
    /// <summary>
    /// 与网页端对齐的 game mode 目录：调 unified/modes 接口拉取真实模式编码，
    /// 按 (category, teamSize) 匹配出查询 season 用的 modeCode（如天选双排=5000200）。
    /// 网页端正是从 modes 接口动态取 code，而不是用硬编码的 miniProgram 编码。
    /// 拉取失败或未命中时回退 <see cref="GameModeExtensions.ToHeyBoxBattleTid"/>（单排/三排/无尽试炼仍正确）。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public sealed class GameModeCatalog
    {
        private readonly object _sync = new();
        private IReadOnlyList<UnifiedMode>? _modes;
        private Task<IReadOnlyList<UnifiedMode>>? _inflight;

        /// <summary>
        /// 返回指定 GameMode 在 unified/modes 里的真实编码（modeCode）。优先动态获取，失败回退硬编码。
        /// </summary>
        public async Task<string> GetModeCodeAsync(GameMode gameMode, CancellationToken ct)
        {
            var modes = await GetModesAsync(ct).ConfigureAwait(false);
            var targetCategory = ToCategoryString(gameMode.GetCategory());
            var targetSize = (int)gameMode.GetTeamSize();

            foreach (var m in modes)
            {
                if (m.TeamSize == targetSize
                    && string.Equals(m.Category, targetCategory, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(m.Code))
                {
                    return m.Code;
                }
            }

            return gameMode.ToHeyBoxBattleTid().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private async Task<IReadOnlyList<UnifiedMode>> GetModesAsync(CancellationToken ct)
        {
            var cached = _modes;
            if (cached != null) return cached;

            Task<IReadOnlyList<UnifiedMode>> task;
            lock (_sync)
            {
                cached = _modes;
                if (cached != null) return cached;
                task = _inflight ??= LoadModesCoreAsync(ct);
            }

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 上游并发取消不代表调用方取消：重试一次。
                return await LoadModesCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                // 成功或失败都清掉 in-flight，允许下次重试。
                lock (_sync) _inflight = null;
            }
        }

        private async Task<IReadOnlyList<UnifiedMode>> LoadModesCoreAsync(CancellationToken ct)
        {
            try
            {
                var resp = await NarakaApiClient.GetGameModesAsync(source: null, ct).ConfigureAwait(false);
                var modes = UnifiedMapper.MapModes(resp);
                lock (_sync) _modes = modes;
                return modes;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    $"{nameof(GameModeCatalog)}.{nameof(LoadModesCoreAsync)}",
                    $"modes 接口获取失败，回退硬编码 modeCode: {ex.Message}");
                return Array.Empty<UnifiedMode>();
            }
        }

        /// <summary>把 GameModeCategory 枚举映射为 unified/modes 返回的 category 字符串（rank/match/tianren/fun）。</summary>
        private static string ToCategoryString(GameModeCategory category)
        {
            return category switch
            {
                GameModeCategory.Rank => "rank",
                GameModeCategory.Match => "match",
                GameModeCategory.Tianren => "tianren",
                GameModeCategory.Fun => "fun",
                _ => string.Empty,
            };
        }
    }
}
