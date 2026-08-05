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
    /// player_prefs.txt 里的 <c>login_md5</c> 字段并不可靠——网易平台压根不写它——所以只当兜底。</para>
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

        // Player.log 可能几十~几百 KB；只读末尾一段就够找最后一条 "Login Success"。
        private const int PlayerLogTailBytes = 64 * 1024;

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
        /// 从 Player.log 末尾找最后一条 <c>Login Success--&gt;&gt; aid=&lt;UID&gt;</c> 记录，返回 aid。
        /// 找不到或读失败返回 null，让上层走 heuristic 兜底。
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

                var tailLen = (int)Math.Min(fs.Length, PlayerLogTailBytes);
                if (tailLen == 0) return null;

                fs.Seek(-tailLen, SeekOrigin.End);
                var buffer = new byte[tailLen];
                var read = await fs.ReadAsync(buffer.AsMemory(0, tailLen)).ConfigureAwait(false);
                var tail = Encoding.UTF8.GetString(buffer, 0, read);

                // 从后往前找最后一个匹配。日志里一次会话可能多次 Login Success（重连），取最新的。
                var idx = tail.LastIndexOf(LoginSuccessMarker, StringComparison.Ordinal);
                if (idx < 0) return null;

                var aidStart = idx + LoginSuccessMarker.Length;
                var aidEnd = aidStart;
                while (aidEnd < tail.Length && char.IsDigit(tail[aidEnd])) aidEnd++;
                if (aidEnd == aidStart) return null;

                return tail.Substring(aidStart, aidEnd - aidStart);
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

            // 空 UID 段（account_prefs_=login_md5,XXX）里的 md5 = "会话 token"，不总是 = 当前账号
            // login_md5，但保留以备兜底 tie-break。
            string? activeLoginMd5 = null;
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
                    if (uid.Length == 0)
                    {
                        if (kv.TryGetValue("login_md5", out var md5))
                            activeLoginMd5 = md5;
                    }
                    else
                    {
                        uidSections.Add((uid, kv));
                    }
                }
                else if (sectionKey == GlobalPrefsKey)
                {
                    var kv = ParseSemicolonPairs(sectionValue);
                    if (kv.TryGetValue("serverId", out var sid)) result.ServerId = sid;
                    if (kv.TryGetValue("maxMember", out var mmStr) && int.TryParse(mmStr, out var mm))
                        result.MaxMember = mm;
                }
            }

            var active = PickActiveAccount(uidSections, activeAid, activeLoginMd5);
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
        /// 3) 兜底 heuristic（Player.log 缺失时）：优先取有 <c>login_md5</c> 字段的段；多段都有则用
        ///    空 UID 段 md5 精确匹配；再不行取字段数最多的段。
        /// </summary>
        private static (string uid, Dictionary<string, string> kv)? PickActiveAccount(
            List<(string uid, Dictionary<string, string> kv)> uidSections,
            string? activeAid,
            string? activeLoginMd5)
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

            // 3) 兜底 heuristic：优先"含 login_md5"的段。
            var loggedIn = new List<(string uid, Dictionary<string, string> kv)>();
            foreach (var section in uidSections)
            {
                if (section.kv.ContainsKey("login_md5"))
                    loggedIn.Add(section);
            }

            if (loggedIn.Count == 1) return loggedIn[0];

            if (loggedIn.Count > 1)
            {
                if (!string.IsNullOrEmpty(activeLoginMd5))
                {
                    foreach (var section in loggedIn)
                    {
                        if (section.kv.TryGetValue("login_md5", out var md5) && md5 == activeLoginMd5)
                            return section;
                    }
                }
                return PickMostPopulated(loggedIn);
            }

            // 全部段都没 login_md5：无从判断当前账号，返回 null，UI 显示空。
            return null;
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
