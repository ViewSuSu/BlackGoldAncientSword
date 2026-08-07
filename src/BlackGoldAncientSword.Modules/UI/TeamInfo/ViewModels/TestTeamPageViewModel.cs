using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels
{
    /// <summary>
    /// Debug 测试页（测试三排 / 测试双排）共用的 ViewModel。
    /// 本地用户卡（三排中间、双排右侧）用本地用户 UID 走后端真实查询；
    /// 其余卡用静态 mock 数据（<see cref="MockTeamData"/>），使卡片间产生真实数据差异，
    /// 便于在无需进入英雄选择阶段时排查 TeamMemberCard 渲染与 diff 对比效果。
    /// 行模板取本地用户查询返回的 Metrics（与正式页以中间卡为模板一致），mock 卡用同 key 填充。
    /// </summary>
    public class TestTeamPageViewModel : ViewModelBase
    {
        private readonly TeamMemberLoader _memberLoader;
        private readonly IPlayerPrefsService _playerPrefsService;
        private readonly IUIDispatcher _uiDispatcher;
        private readonly IClipboardService _clipboard;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly ITipMessageService _tipMessage;
        private CancellationTokenSource? _loadCts;

        public TestTeamPageViewModel(
            TeamMemberLoader memberLoader,
            IPlayerPrefsService playerPrefsService,
            IUIDispatcher uiDispatcher,
            IClipboardService clipboard,
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
        {
            _memberLoader = memberLoader;
            _playerPrefsService = playerPrefsService;
            _uiDispatcher = uiDispatcher;
            _clipboard = clipboard;
            _localizedText = localizedText;
            _tipMessage = tipMessage;
            Members = new ObservableCollection<TeamMemberInfo>();
        }

        /// <summary>成员卡片数据（三排 3 个 / 双排 2 个）。本地用户卡为真实查询，其余为 mock。</summary>
        public ObservableCollection<TeamMemberInfo> Members { get; }

        /// <summary>统计行（含 diff），卡片统计行按 MemberIndex 取列值，diff 列按 DiffLeft/DiffRight 渲染。</summary>
        public ObservableCollection<MergedStatRow> StatRows { get; } = new();

        /// <summary>相邻卡之间是否有 diff 列（三排有 2 列、双排有 1 列）。</summary>
        public bool HasDiffLeft => Members.Count >= 2;
        public bool HasDiffRight => Members.Count >= 3;

        /// <summary>本地用户卡在三排中的槽位（三排=1 中间，双排=1 右侧），子类可覆盖。</summary>
        protected virtual int LocalUserIndex => 1;

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            _ = LoadAsync();
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            CancelAndDispose(ref _loadCts);
            base.OnNavigatedFromExecute(navigationContext);
        }

        private async Task LoadAsync()
        {
            CancelAndDispose(ref _loadCts);
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            Members.Clear();
            StatRows.Clear();
            // 先按默认槽位建卡，随后填充数据。
            for (int i = 0; i < Capacity; i++)
                Members.Add(new TeamMemberInfo(_clipboard, _localizedText, _tipMessage));
            RaiseHasDiff();

            try
            {
                await _playerPrefsService.LoadAsync();
                var prefs = _playerPrefsService.Current;
                var uid = prefs.PlayerId;
                var name = prefs.OriginalPlayerName;
                var localIdx = LocalUserIndex;

                // 填 mock 卡（左右）：立即填充，让 diff 有数据可算。
                for (int i = 0; i < Members.Count; i++)
                {
                    if (i == localIdx) continue;
                    Members[i] = CreateMockMember(i);
                }

                // 本地用户卡：真实查询。
                if (string.IsNullOrWhiteSpace(uid))
                {
                    SetLocalUserStatus("未读取到本地 UID（player_prefs）");
                }
                else
                {
                    await LoadLocalUserAsync(localIdx, name, uid, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TestTeamPageViewModel)}.{nameof(LoadAsync)}");
                SetLocalUserStatus("查询失败");
            }
        }

        /// <summary>卡片数量：三排 3、双排 2，子类覆盖。</summary>
        protected virtual int Capacity => 3;

        private TeamMemberInfo CreateMockMember(int index)
        {
            return index == 0
                ? MockTeamData.CreateLeftMember(_clipboard, _localizedText, _tipMessage)
                : MockTeamData.CreateRightMember(_clipboard, _localizedText, _tipMessage);
        }

        private async Task LoadLocalUserAsync(int localIdx, string name, string uid, CancellationToken ct)
        {
            try
            {
                var season = SeasonCatalog.All().FirstOrDefault()?.Code;
                var loaded = await _memberLoader.LoadAsync(name, season, GameModeCategory.Rank, TeamSize.Trio, ct, uid)
                    .ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (localIdx >= Members.Count) return;
                    var member = Members[localIdx];
                    if (loaded.Failed)
                    {
                        member.StatusText = string.IsNullOrWhiteSpace(loaded.FailMsg) ? "查询失败" : loaded.FailMsg!;
                    }
                    else
                    {
                        member.Level = loaded.Level;
                        member.UID = loaded.UID;
                        if (!string.IsNullOrWhiteSpace(loaded.UserName))
                            member.DisplayName = loaded.UserName;
                        member.AvatarUrl = loaded.AvatarUrl;
                        member.RankName = loaded.Stats?.RankName ?? member.RankName;
                        member.RankIcon = loaded.Stats?.RankIcon ?? member.RankIcon;
                        member.RankScore = loaded.Stats?.RankScore ?? 0;
                        member.PageRankName = loaded.Stats?.PageRankName ?? member.PageRankName;
                        member.PageStarCount = loaded.Stats?.PageStarCount ?? 0;
                        member.PageHasStars = loaded.Stats?.PageHasStars ?? false;
                        member.RankTierScore = loaded.Stats?.RankTierScore ?? 0;
                        member.Stats.Clear();
                        member.Metrics.Clear();
                        if (loaded.Stats != null)
                        {
                            foreach (var kv in loaded.Stats.Stats)
                                member.Stats[kv.Key] = kv.Value;
                            member.Metrics.AddRange(loaded.Stats.Metrics);
                        }
                        member.StatusText = "";
                    }
                    BuildStatRows();
                });
            }
            catch (OperationCanceledException) { }
            catch (System.Exception ex)
            {
                AppLog.Error(ex, $"{nameof(TestTeamPageViewModel)}.{nameof(LoadLocalUserAsync)}");
                await _uiDispatcher.InvokeAsync(() =>
                {
                    if (localIdx < Members.Count)
                        Members[localIdx].StatusText = "查询失败";
                });
            }
        }

        /// <summary>
        /// 以本地用户卡（三排中间 / 双排右侧）Metrics 为行模板重建统计行（与正式页以中间卡为模板一致）。
        /// diff：左 mock vs 中 本地、中 本地 vs 右 mock。
        /// </summary>
        private void BuildStatRows()
        {
            StatRows.Clear();
            var localIdx = LocalUserIndex;
            var template = (localIdx < Members.Count && Members[localIdx].Metrics.Count > 0)
                ? Members[localIdx].Metrics
                : Members.FirstOrDefault(m => m.Metrics.Count > 0)?.Metrics
                  ?? new List<PlayerStatMetric>();

            foreach (var metric in template)
            {
                var row = new MergedStatRow
                {
                    Label = metric.Label,
                    Val0 = GetStatVal(0, metric.Key),
                    Val1 = GetStatVal(1, metric.Key),
                    Val2 = GetStatVal(2, metric.Key),
                };
                FillDiff(row, isLeft: true, 0, 1, (metric.Key, metric.Label, metric.IsPercent));
                FillDiff(row, isLeft: false, 1, 2, (metric.Key, metric.Label, metric.IsPercent));
                StatRows.Add(row);
            }

            var rankRow = new MergedStatRow
            {
                Label = "段位分",
                Val0 = GetRankVal(0),
                Val1 = GetRankVal(1),
                Val2 = GetRankVal(2),
            };
            FillDiff(rankRow, isLeft: true, 0, 1, (RankRowKey, "段位分", false));
            FillDiff(rankRow, isLeft: false, 1, 2, (RankRowKey, "段位分", false));
            StatRows.Add(rankRow);
        }

        private const string RankRowKey = "__rank__";

        private string GetStatVal(int idx, string key)
        {
            if (idx >= Members.Count) return "-";
            return Members[idx].Stats.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : "-";
        }

        private string GetRankVal(int idx)
        {
            if (idx >= Members.Count) return "-";
            return Members[idx].RankScore > 0 ? Members[idx].RankScore.ToString("F0") : "-";
        }

        private void FillDiff(MergedStatRow row, bool isLeft, int aIdx, int bIdx,
            (string Key, string Label, bool IsPercent) def)
        {
            if (aIdx >= Members.Count || bIdx >= Members.Count) return;
            double av, bv;
            if (def.Key == RankRowKey)
            {
                av = Members[aIdx].RankScore;
                bv = Members[bIdx].RankScore;
            }
            else
            {
                av = Members[aIdx].Stats.TryGetValue(def.Key, out var al) ? TryParseDouble(al) : 0;
                bv = Members[bIdx].Stats.TryGetValue(def.Key, out var bl) ? TryParseDouble(bl) : 0;
            }
            var diff = av - bv;
            const string fmt = "0.##";
            string text, color;
            if (System.Math.Abs(diff) < 0.001) { text = "0"; color = "#999999"; }
            else if (diff > 0) { text = def.IsPercent ? $"+{diff:F1}%" : $"+{diff.ToString(fmt)}"; color = "#22AA22"; }
            else { text = def.IsPercent ? $"{diff:F1}%" : $"{diff.ToString(fmt)}"; color = "#DD3333"; }

            if (isLeft) { row.DiffLeftText = text; row.DiffLeftColor = color; }
            else { row.DiffRightText = text; row.DiffRightColor = color; }
        }

        private static double TryParseDouble(string s)
        {
            if (double.TryParse(s?.Replace("%", ""), out var v)) return v;
            return 0;
        }

        private void SetLocalUserStatus(string text)
        {
            _ = _uiDispatcher.InvokeAsync(() =>
            {
                var localIdx = LocalUserIndex;
                if (localIdx < Members.Count)
                {
                    Members[localIdx].StatusText = text;
                    Members[localIdx].IsLoading = false;
                }
            });
        }

        private void RaiseHasDiff()
        {
            RaisePropertyChanged(nameof(HasDiffLeft));
            RaisePropertyChanged(nameof(HasDiffRight));
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); }
            catch { }
            cts.Dispose();
            cts = null;
        }
    }

    /// <summary>测试三排页 ViewModel：3 卡，中间=本地用户真实查询，左右=mock。</summary>
    public class TestTrioPageViewModel : TestTeamPageViewModel
    {
        public TestTrioPageViewModel(
            TeamMemberLoader memberLoader,
            IPlayerPrefsService playerPrefsService,
            IUIDispatcher uiDispatcher,
            IClipboardService clipboard,
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
            : base(memberLoader, playerPrefsService, uiDispatcher, clipboard, localizedText, tipMessage) { }

        protected override int Capacity => 3;
        protected override int LocalUserIndex => 1;
    }

    /// <summary>测试双排页 ViewModel：2 卡，右=本地用户真实查询，左=mock。</summary>
    public class TestDuoPageViewModel : TestTeamPageViewModel
    {
        public TestDuoPageViewModel(
            TeamMemberLoader memberLoader,
            IPlayerPrefsService playerPrefsService,
            IUIDispatcher uiDispatcher,
            IClipboardService clipboard,
            ILocalizedTextProvider localizedText,
            ITipMessageService tipMessage)
            : base(memberLoader, playerPrefsService, uiDispatcher, clipboard, localizedText, tipMessage) { }

        protected override int Capacity => 2;
        protected override int LocalUserIndex => 1;
    }
}
