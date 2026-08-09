using System;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// <see cref="IUpdateService.UpdateAvailabilityChanged"/> 的事件参数，
    /// 在布尔可用性之外携带本次检查的 <see cref="UpdateCheckSource"/>，供订阅方决定 UI 呈现。
    /// </summary>
    public sealed class UpdateAvailabilityChangedEventArgs : EventArgs
    {
        public UpdateAvailabilityChangedEventArgs(bool isAvailable, UpdateCheckSource source)
        {
            IsAvailable = isAvailable;
            Source = source;
        }

        /// <summary>是否有新版可用。</summary>
        public bool IsAvailable { get; }

        /// <summary>触发该次检查的来源。</summary>
        public UpdateCheckSource Source { get; }
    }
}
