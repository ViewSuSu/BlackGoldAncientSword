using System.Diagnostics;

namespace BlackGoldAncientSword.Framework.Core.Extensions;

/// <summary>
/// 替换裸 _ = 丢弃 Task 的模式，确保异常至少被输出到调试日志。
/// 用法：task.SafeFireAndForget("context name")
/// </summary>
public static class TaskExtensions
{
    public static void SafeFireAndForget(this System.Threading.Tasks.Task task, string? context = null)
    {
        if (task is null) return;
        task.ContinueWith(t =>
        {
            var ex = t.Exception?.Flatten().InnerException ?? t.Exception?.InnerException;
            if (ex is not null)
            {
                var ctx = context is not null ? $"[{context}]" : string.Empty;
                Debug.WriteLine($"[FireAndForget] Unhandled async exception{ctx}: {ex}");
            }
        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }
}
