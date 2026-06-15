using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Services
{
    /// <summary>
    /// OCR 触发与节流协调器。封装"等首延迟 → 周期性重试识别 → 命中即返回"流程，
    /// 让 VM 摆脱循环细节，仅暴露"识别一次 / 流式拿到首个有效结果"两个语义。
    /// </summary>
    [Component(ComponentLifetime.Singleton)]
    public class TeamOcrCoordinator
    {
        private readonly ITeamInfoOcrService _ocr;

        public TeamOcrCoordinator(ITeamInfoOcrService ocr)
        {
            _ocr = ocr;
        }

        /// <summary>
        /// 立即触发一次 OCR 识别。
        /// </summary>
        public Task<string[]> RecognizeOnceAsync(CancellationToken ct) => _ocr.RecognizeTeamMembersAsync(ct);

        /// <summary>
        /// 进入英雄选择阶段后的识别循环：先等 <paramref name="initialDelay"/> 让 UI 稳定，
        /// 然后每 <paramref name="retryInterval"/> 重试一次，直到拿到非空结果或被取消。
        /// 返回首次拿到的非空结果；若被取消则抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        public async Task<string[]> WaitForFirstRecognitionAsync(
            TimeSpan initialDelay,
            TimeSpan retryInterval,
            CancellationToken ct)
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var names = await _ocr.RecognizeTeamMembersAsync(ct).ConfigureAwait(false);
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
    }
}
