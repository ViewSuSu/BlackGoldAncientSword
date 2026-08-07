namespace BlackGoldAncientSword.GameMonitor.Services.Implementation
{
    using System.Diagnostics;
    using System.Text;
    using BlackGoldAncientSword.Framework.Core.Attributes;
    using BlackGoldAncientSword.Framework.Core.Extensions;
    using BlackGoldAncientSword.Framework.Core.Infrastructure;

    /// <summary>
    /// 玩家偏好数据服务。从永劫无间的 player_prefs.txt 异步读取玩家信息。
    /// </summary>
    /// <remarks>
    /// <para>Steam 与网易客户端共用同一份 <c>%LocalAppDataLow%\24Entertainment\Naraka\player_prefs.txt</c>。
    /// 同时登过两个平台的用户，文件里会残留多个 <c>account_prefs_&lt;UID&gt;=...</c> 段。旧实现无脑
    /// 遍历、最后一段覆盖胜出，会串号（登了 Steam 仍显示网易账户）。</para>
    ///
    /// <para><b>权威判据是 Player.log 里的 "Login Success-->> aid=&lt;UID&gt;" 记录</b>：
    /// 无论 Steam 还是网易，游戏成功登录时都会往同目录的 <c>Player.log</c> 写一行登录成功日志，
    /// 里面的 <c>aid</c> 就是当前活跃账号的 UID。文件末尾最后一条 "Login Success" 就是"最新登录记录"。
    /// player_prefs.txt 里的 <c>login_md5</c> 字段并不可靠——跨平台都可能残留，只当兜底。</para>
    ///
    /// <para>"用户切账号后 Current 陈旧"的问题：调用方在需要最新值的时机（例如进入战绩页）主动
    /// <c>await LoadAsync()</c> 即可，不再挂 FileSystemWatcher。</para>
    /// </remarks>
    [Component(ComponentLifetime.Singleton)]
    public class PlayerPrefsService : IPlayerPrefsService
    {
        private static readonly string NarakaDataDir =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "24Entertainment", "Naraka");

        private static readonly string FilePath = System.IO.Path.Combine(NarakaDataDir, "player_prefs.txt");
        private static readonly string PlayerLogPath = System.IO.Path.Combine(NarakaDataDir, "Player.log");

        private const string AccountPrefsPrefix = "account_prefs_";
        private const string GlobalPrefsKey = "global_prefs_key";
        private const string LoginSuccessMarker = "Login Success-->> aid=";

        public PlayerPrefsData Current { get; private set; } = new();

        public PlayerPrefsService()
        {
            // 启动快照：先读一次，后续调用方按需 await LoadAsync() 主动刷新。
            LoadAsync().SafeFireAndForget(nameof(PlayerPrefsService) + "." + nameof(LoadAsync));
        }

        public async Task LoadAsync()
        {
            try
            {
                if (!System.IO.File.Exists(FilePath))
                    return;

                var lines = await System.IO.File.ReadAllLinesAsync(FilePath).ConfigureAwait(false);

                // 拿"最新登录记录"里的 aid：这是权威信号，覆盖率高、不受平台差异影响。
                // 读 Player.log 失败 / 找不到 aid 都不致命，ParseLines 会走 heuristic 兜底。
                var activeAid = await TryReadActiveAidFromPlayerLogAsync().ConfigureAwait(false);

                var result = ParseLines(lines, activeAid);
                result.IsLoaded = true;
                Current = result;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // 解析失败不影响主流程；保留上一次成功的 Current。
                AppLog.Error(ex, $"{nameof(PlayerPrefsService)}.{nameof(LoadAsync)}");
            }
        }

        /// <summary>
        /// 从 Player.log 末尾往前搜索最后一条 <c>Login Success--&gt;&gt; aid=&lt;UID&gt;</c> 记录，返回 aid。
        /// 找不到或读失败返回 null，让上层走 heuristic 兜底。
        /// <para>
        /// 登录记录可能出现在文件任意位置（多次启动后滚动、会话中途重连），所以从末尾
        /// 按块向前翻：每块 64KB，下一块与上一块末尾重叠 <see cref="LoginSuccessMarker"/> 长度，
        /// 保证跨块边界的匹配不被漏掉。找到的**第一块**里取 LastIndexOf 即最后一条登录记录。
        /// </para>
        /// </summary>
        private static async Task<string?> TryReadActiveAidFromPlayerLogAsync()
        {
            try
            {
                if (!System.IO.File.Exists(PlayerLogPath))
                    return null;

                // 游戏进程持有独占 write，我们必须以 FileShare.ReadWrite 打开才能并发读。
                await using var fs = new FileStream(
                    PlayerLogPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);

                var fileLen = fs.Length;
                if (fileLen == 0) return null;

                const int chunkSize = 64 * 1024;
                var overlap = LoginSuccessMarker.Length;
                var buffer = new byte[chunkSize];

                long end = fileLen;
                while (end > 0)
                {
                    var start = Math.Max(0, end - chunkSize);
                    fs.Seek(start, SeekOrigin.Begin);
                    var read = await fs.ReadAsync(buffer.AsMemory(0, (int)(end - start))).ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(buffer, 0, read);

                    // 从后往前找本块内最后一条匹配；登录记录通常距当前位置较近。
                    var idx = text.LastIndexOf(LoginSuccessMarker, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        var aidStart = idx + LoginSuccessMarker.Length;
                        var aidEnd = aidStart;
                        while (aidEnd < text.Length && char.IsDigit(text[aidEnd])) aidEnd++;
                        if (aidEnd > aidStart)
                            return text.Substring(aidStart, aidEnd - aidStart);
                    }

                    if (start == 0) break; // 已翻到文件头

                    // 下一块末尾 = 本块开头 + overlap，重叠覆盖可能跨边界的标记。
                    end = start + overlap;
                }

                return null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                AppLog.Error(ex, $"{nameof(PlayerPrefsService)}.{nameof(TryReadActiveAidFromPlayerLogAsync)}");
                return null;
            }
        }

        private static PlayerPrefsData ParseLines(string[] lines, string? activeAid)
        {
            var result = new PlayerPrefsData();

            var uidSections = new List<(string uid, Dictionary<string, string> kv)>();

            foreach (var line in lines)
            {
                var eqIdx = line.IndexOf('=');
                if (eqIdx < 0) continue;

                var sectionKey = line.Substring(0, eqIdx);
                var sectionValue = line.Substring(eqIdx + 1);

                if (sectionKey.StartsWith(AccountPrefsPrefix, StringComparison.Ordinal))
                {
                    var uid = sectionKey.Substring(AccountPrefsPrefix.Length);
                    var kv = ParseSemicolonPairs(sectionValue);
                    if (uid.Length > 0)
                        uidSections.Add((uid, kv));
                }
                else if (sectionKey == GlobalPrefsKey)
                {
                    var kv = ParseSemicolonPairs(sectionValue);
                    if (kv.TryGetValue("serverId", out var sid)) result.ServerId = sid;
                    if (kv.TryGetValue("maxMember", out var mmStr) && int.TryParse(mmStr, out var mm))
                        result.MaxMember = mm;
                }
            }

            var active = PickActiveAccount(uidSections, activeAid);
            if (active != null)
            {
                if (active.Value.kv.TryGetValue("player_name", out var name))
                {
                    result.PlayerName = name;
                    result.OriginalPlayerName = name;
                }
                // 本地用户角色 ID：取活跃账号段内 player_id 字段（与 player_name 在同一段配对出现，
                // 如 player_id,l77c000015949400120163 / player_name,爱的供养丶）。该值含字母前缀，
                // 是永劫角色 ID，SearchRecord 接口"支持昵称或角色ID"，本地用户查询用它绕过名字查不到
                // 的问题（用户名可能重名/查无，角色 ID 唯一）。
                // 极少数段缺 player_id 时回退到 account_prefs_<UID> 的 section-key（登录账号 aid）。
                if (active.Value.kv.TryGetValue("player_id", out var id) && !string.IsNullOrEmpty(id))
                    result.PlayerId = id;
                else if (!string.IsNullOrEmpty(active.Value.uid))
                    result.PlayerId = active.Value.uid;
                if (active.Value.kv.TryGetValue("player_level", out var lvl) && int.TryParse(lvl, out var level))
                    result.PlayerLevel = level;
            }

            return result;
        }

        /// <summary>
        /// 挑当前活跃账号。层级：
        /// 1) <b>权威：Player.log 里 "Login Success-->> aid=X" 的 aid 与 UID 完全匹配</b> —— 直接返回。
        /// 2) 单段兜底：只有一段 UID，直接取它（单平台用户常态，与旧行为等价）。
        /// 3) 多段兜底（Player.log 缺失时）：不偏好 login_md5（网易端不写此字段，偏好它会导致
        ///    Steam 段总是胜出）。直接取键值对最多的段，通常就是最近活跃的账号。
        /// </summary>
        private static (string uid, Dictionary<string, string> kv)? PickActiveAccount(
            List<(string uid, Dictionary<string, string> kv)> uidSections,
            string? activeAid)
        {
            if (uidSections.Count == 0) return null;

            // 1) 权威：Player.log 里 Login Success 的 aid 直接匹配 UID。
            if (!string.IsNullOrEmpty(activeAid))
            {
                foreach (var section in uidSections)
                {
                    if (section.uid == activeAid) return section;
                }
                // aid 找到但 prefs 段还没落盘（极少见）——继续兜底。
            }

            // 2) 单段兜底。
            if (uidSections.Count == 1) return uidSections[0];

            // 3) 多段兜底：取 kv 数最多的段（字段越全通常就是最近活跃的账号）。
            // 不偏好 login_md5：网易端不写此字段，偏好它会固定错选 Steam 段。
            return PickMostPopulated(uidSections);
        }

        private static (string uid, Dictionary<string, string> kv) PickMostPopulated(
            List<(string uid, Dictionary<string, string> kv)> sections)
        {
            var best = sections[0];
            var bestScore = best.kv.Count;
            for (var i = 1; i < sections.Count; i++)
            {
                if (sections[i].kv.Count > bestScore)
                {
                    best = sections[i];
                    bestScore = sections[i].kv.Count;
                }
            }
            return best;
        }

        private static Dictionary<string, string> ParseSemicolonPairs(string value)
        {
            var dict = new Dictionary<string, string>();
            var pairs = value.Split(';');
            foreach (var pair in pairs)
            {
                var commaIdx = pair.IndexOf(',');
                if (commaIdx < 0) continue;
                var key = pair.Substring(0, commaIdx);
                var val = pair.Substring(commaIdx + 1);
                dict[key] = val;
            }
            return dict;
        }
    }
}
