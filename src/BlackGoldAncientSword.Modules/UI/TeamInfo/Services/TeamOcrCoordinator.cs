using System.Diagnostics;
using BlackGoldAncientSword.Framework.Core.Attributes;
using BlackGoldAncientSword.Framework.Core.Consts;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// OCR 触发与节流协调器。封装"等首延迟 → 周期性重试识别 → 命中即返回"流程，
    /// 让 VM 摆脱循环细节，仅暴露"识别一次 / 流式拿到首个有效结果"两个语义。
    /// 支持按 <see cref="TeamSize"/> 路由到三排/双排识别逻辑，也支持自动检测队伍规模。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class TeamOcrCoordinator
    {
        private readonly ITeamInfoOcrService _ocr;

        public TeamOcrCoordinator(ITeamInfoOcrService ocr)
        {
            _ocr = ocr;
        }

        /// <summary>立即触发一次三排 OCR 识别（默认）。</summary>
        public Task<string[]> RecognizeOnceAsync(CancellationToken ct)
            => RecognizeOnceAsync(TeamSize.Trio, ct);

        /// <summary>立即触发一次指定队伍规模的 OCR 识别。</summary>
        public Task<string[]> RecognizeOnceAsync(TeamSize teamSize, CancellationToken ct)
            => RecognizeInternalAsync(teamSize, ct);

        /// <summary>立即触发一次自动检测队伍规模的 OCR 识别。</summary>
        public Task<string[]> RecognizeAutoAsync(CancellationToken ct)
            => _ocr.RecognizeTeamMembersAutoAsync(ct);

        /// <summary>
        /// 进入英雄选择阶段后的识别循环：先等 <paramref name="initialDelay"/> 让 UI 稳定，
        /// 然后每 <paramref name="retryInterval"/> 重试一次，直到拿到非空结果或被取消。
        /// 返回首次拿到的非空结果；若被取消则抛 <see cref="OperationCanceledException"/>。
        /// 默认使用三排识别逻辑。
        /// </summary>
        public Task<string[]> WaitForFirstRecognitionAsync(
            TimeSpan initialDelay,
            TimeSpan retryInterval,
            CancellationToken ct)
            => WaitForFirstRecognitionAsync(initialDelay, retryInterval, TeamSize.Trio, ct);

        /// <summary>带指定队伍规模的识别循环。</summary>
        public async Task<string[]> WaitForFirstRecognitionAsync(
            TimeSpan initialDelay,
            TimeSpan retryInterval,
            TeamSize teamSize,
            CancellationToken ct)
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var names = await RecognizeInternalAsync(teamSize, ct).ConfigureAwait(false);
                    if (names.Length > 0) return names;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(TeamOcrCoordinator)}] recognize error: {ex.Message}");
                }

                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(retryInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }

            ct.ThrowIfCancellationRequested();
            return Array.Empty<string>();
        }

        /// <summary>
        /// 自动检测队伍规模的识别循环。
        /// 每次识别前先用三排区域小区域试识别，有文本则用三排逻辑，否则用双排逻辑。
        /// </summary>
        public async Task<string[]> WaitForAutoRecognitionAsync(
            TimeSpan initialDelay,
            TimeSpan retryInterval,
            CancellationToken ct)
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var names = await _ocr.RecognizeTeamMembersAutoAsync(ct).ConfigureAwait(false);
                    if (names.Length > 0) return names;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{nameof(TeamOcrCoordinator)}] auto recognize error: {ex.Message}");
                }

                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(retryInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }

            ct.ThrowIfCancellationRequested();
            return Array.Empty<string>();
        }

        /// <summary>根据队伍规模路由到对应的 OCR 方法。</summary>
        private Task<string[]> RecognizeInternalAsync(TeamSize teamSize, CancellationToken ct)
            => teamSize switch
            {
                TeamSize.Duo => _ocr.RecognizeDuoTeamMembersAsync(ct),
                _ => _ocr.RecognizeTeamMembersAsync(ct),
            };
    }
}

