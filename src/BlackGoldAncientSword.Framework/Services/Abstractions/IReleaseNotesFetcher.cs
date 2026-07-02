using System.Threading.Tasks;

namespace BlackGoldAncientSword.Framework.Services.Abstractions
{
    /// <summary>
    /// 拉取 Gitee release 描述（tag body），不走 /api/v5 API，避免未鉴权 IP 命中 60 req/min 限流。
    /// </summary>
    public interface IReleaseNotesFetcher
    {
        /// <summary>
        /// 拉取指定 tag 的 release 描述。失败返 null，调用方按 null 做隐藏处理即可，不抛异常。
        /// </summary>
        /// <param name="tag">tag 名（含或不含 v 前缀均可，实现内部会规范化）。</param>
        Task<string?> FetchAsync(string tag);
    }
}
