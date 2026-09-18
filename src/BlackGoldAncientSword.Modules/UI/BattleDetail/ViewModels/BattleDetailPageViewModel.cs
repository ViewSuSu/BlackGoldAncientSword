using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.Stats.Services;

namespace BlackGoldAncientSword.Modules.UI.BattleDetail.ViewModels
{
    /// <summary>
    /// 对局详情浮层：拉取归一化后的 <see cref="UnifiedBattleDetail"/>。
    /// 小黑盒的详情响应实测只带个人数据（<c>result</c> 顶层），<c>all_team</c> 是空数组，
    /// 因此 team/top5 集合为空、UI 侧对应 Tab 不显示（见 <see cref="ApplyTeam"/>）。
    /// </summary>
    public class BattleDetailPageViewModel : ViewModelBase
    {
        private readonly PlayerStatsLoader _loader;
        private readonly IUIDispatcher _uiDispatcher;
        private CancellationTokenSource? _cts;

        public BattleDetailPageViewModel(PlayerStatsLoader loader, IUIDispatcher uiDispatcher)
        {
            _loader = loader;
            _uiDispatcher = uiDispatcher;
        }

        // === Loading ===
        private bool _isLoading = true;
        public bool IsLoading { get => _isLoading; set { _isLoading = value; RaisePropertyChanged(); } }

        // === Tab 切换 ===
        private string _selectedTab = "Personal";
        public string SelectedTab { get => _selectedTab; set { _selectedTab = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsPersonalTab)); RaisePropertyChanged(nameof(IsTeamTab)); RaisePropertyChanged(nameof(IsTop5Tab)); } }
        public bool IsPersonalTab => SelectedTab == "Personal";
        public bool IsTeamTab => SelectedTab == "Team";
        public bool IsTop5Tab => SelectedTab == "Top5";

        // === Tab 可见性（与网页一致：队伍/前五数据缺失时不显示对应 tab） ===
        private bool _hasTeam;
        public bool HasTeam { get => _hasTeam; set { _hasTeam = value; RaisePropertyChanged(); } }

        private bool _hasTop5;
        public bool HasTop5 { get => _hasTop5; set { _hasTop5 = value; RaisePropertyChanged(); } }

        private DelegateCommand<string>? _switchTabCommand;
        public DelegateCommand<string> SwitchTabCommand =>
            _switchTabCommand ??= new DelegateCommand<string>(tab => { if (!string.IsNullOrEmpty(tab)) SelectedTab = tab; });

        // === 展开更多数据 ===
        private bool _showMoreStats;
        public bool ShowMoreStats { get => _showMoreStats; set { _showMoreStats = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExpandMoreText)); } }
        public string ExpandMoreText => ShowMoreStats ? "收起更多数据" : "展开更多数据";

        // 有可展开的额外指标时才显示展开按钮（core-data 之外还有 MoreStats）。
        private bool _hasMoreStats;
        public bool HasMoreStats { get => _hasMoreStats; set { _hasMoreStats = value; RaisePropertyChanged(); } }
        private DelegateCommand? _toggleMoreStatsCommand;
        public DelegateCommand ToggleMoreStatsCommand =>
            _toggleMoreStatsCommand ??= new DelegateCommand(() => ShowMoreStats = !ShowMoreStats);

        // === 顶部信息栏 ===
        private string _modeType = string.Empty;
        public string ModeType { get => _modeType; set { _modeType = value; RaisePropertyChanged(); } }

        private string _teamSizeGlyph = string.Empty;
        public string TeamSizeGlyph { get => _teamSizeGlyph; set { _teamSizeGlyph = value; RaisePropertyChanged(); } }

        private string _battleTime = string.Empty;
        public string BattleTime { get => _battleTime; set { _battleTime = value; RaisePropertyChanged(); } }

        /// <summary>本局地图名（详情 result.map_name，如 "龙隐洞天"）。</summary>
        private string _mapName = string.Empty;
        public string MapName
        {
            get => _mapName;
            set { _mapName = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasMapName)); }
        }

        public bool HasMapName => !string.IsNullOrEmpty(_mapName);

        private string _rankText = string.Empty;
        public string RankText { get => _rankText; set { _rankText = value; RaisePropertyChanged(); } }

        /// <summary>结算段位图标（详情 result.level_img）。</summary>
        private string _rankIcon = string.Empty;
        public string RankIcon
        {
            get => _rankIcon;
            set { _rankIcon = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasRankIcon)); }
        }

        public bool HasRankIcon => !string.IsNullOrEmpty(_rankIcon);

        // 段位块（与战绩页 RankScore 列一致）
        private bool _isRankMode;
        public bool IsRankMode { get => _isRankMode; set { _isRankMode = value; RaisePropertyChanged(); } }

        private string _rankDisplayText = string.Empty;
        public string RankDisplayText { get => _rankDisplayText; set { _rankDisplayText = value; RaisePropertyChanged(); } }

        private double _starCount;
        public double StarCount { get => _starCount; set { _starCount = value; RaisePropertyChanged(); } }

        private bool _hasStars;
        public bool HasStars { get => _hasStars; set { _hasStars = value; RaisePropertyChanged(); } }

        private double _scoreNumber;
        public double ScoreNumber { get => _scoreNumber; set { _scoreNumber = value; RaisePropertyChanged(); } }

        private bool _showScoreNumber;
        public bool ShowScoreNumber { get => _showScoreNumber; set { _showScoreNumber = value; RaisePropertyChanged(); } }

        private double _scoreDiff;
        public double ScoreDiff { get => _scoreDiff; set { _scoreDiff = value; RaisePropertyChanged(); } }

        private string _scoreDiffDisplay = string.Empty;
        public string ScoreDiffDisplay { get => _scoreDiffDisplay; set { _scoreDiffDisplay = value; RaisePropertyChanged(); } }

        // === Personal Tab ===
        private string _playerAvatar = string.Empty;
        public string PlayerAvatar { get => _playerAvatar; set { _playerAvatar = value; RaisePropertyChanged(); } }

        private string _playerName = string.Empty;
        public string PlayerName { get => _playerName; set { _playerName = value; RaisePropertyChanged(); } }

        private string _heroName = string.Empty;
        public string HeroName { get => _heroName; set { _heroName = value; RaisePropertyChanged(); } }

        public ObservableCollection<HonorTitleDisplay> HonorTitles { get; } = new();
        public ObservableCollection<CoreDataItem> CoreData { get; } = new();
        public ObservableCollection<StatEntryDisplay> MoreStats { get; } = new();
        public ObservableCollection<WeaponDisplay> Weapons { get; } = new();
        public ObservableCollection<SoulItemDisplay> SoulItems { get; } = new();

        /// <summary>本局有武器伤害明细时才显示"本局武器伤害"区。</summary>
        private bool _hasWeapons;
        public bool HasWeapons { get => _hasWeapons; set { _hasWeapons = value; RaisePropertyChanged(); } }

        // === Team Tab ===
        public ObservableCollection<TeammateDisplay> Teammates { get; } = new();

        // === Top5 Tab ===
        public ObservableCollection<Top5EntryDisplay> Top5Entries { get; } = new();

        // === Commands ===
        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                if (regionManager.Regions.ContainsRegionWithName(GlobalConstant.BattleDetailRegion))
                    regionManager.Regions[GlobalConstant.BattleDetailRegion].RemoveAll();
            });

        // === Navigation ===
        protected override void OnNavigatedToExecute(NavigationContext ctx)
        {
            var p = ctx.Parameters;
            var battleId = p.GetValue<string?>(PageNames.BattleDetailPage);
            var roleId = p.GetValue<string?>("RoleId");
            var server = p.GetValue<string?>("Server") ?? string.Empty;

            // 直接消费 StatsPage 已算好的段位/分数/模式文本，避免详情侧二次计算。
            ModeType = p.GetValue<string?>("ModeCategoryText") ?? string.Empty;
            TeamSizeGlyph = p.GetValue<string?>("ModeTeamSizeText") ?? string.Empty;
            RankDisplayText = p.GetValue<string?>("RankDisplayText") ?? string.Empty;
            StarCount = p.GetValue<double?>("StarCount") ?? 0;
            HasStars = p.GetValue<bool?>("HasStars") ?? false;
            ScoreNumber = p.GetValue<double?>("ScoreNumber") ?? 0;
            ShowScoreNumber = p.GetValue<bool?>("ShowScoreNumber") ?? false;
            ScoreDiff = p.GetValue<double?>("ScoreDiff") ?? 0;
            ScoreDiffDisplay = p.GetValue<string?>("ScoreDiffDisplay") ?? string.Empty;
            IsRankMode = p.GetValue<bool?>("IsRankMode") ?? false;

            // 每次导航前先取消 + Dispose 上一次的 CTS，避免累积泄漏。
            var old = _cts;
            _cts = new CancellationTokenSource();
            try { old?.Cancel(); } catch { }
            old?.Dispose();

            if (string.IsNullOrWhiteSpace(battleId) || string.IsNullOrWhiteSpace(roleId))
            {
                IsLoading = false;
                return;
            }

            SelectedTab = "Personal";
            ShowMoreStats = false;

            var sourceContext = new PlayerSourceContext(roleId!, server);
            LoadAsync(sourceContext, battleId!, _cts.Token).SafeFireAndForget("BattleDetail.Load");
        }

        protected override void OnNavigatedFromExecute(NavigationContext ctx)
        {
            try { _cts?.Cancel(); } catch { }
            // 顺带释放 Personal Tab 里累积的显示数据引用，避免离开详情后旧对局数据继续占内存。
            _uiDispatcher.InvokeAsync(() =>
            {
                HonorTitles.Clear();
                CoreData.Clear();
                MoreStats.Clear();
                Weapons.Clear();
                SoulItems.Clear();
                Teammates.Clear();
                Top5Entries.Clear();
                PlayerAvatar = string.Empty;
                PlayerName = string.Empty;
                HeroName = string.Empty;
                RankText = string.Empty;
                BattleTime = string.Empty;
                MapName = string.Empty;
                RankIcon = string.Empty;
                RankDisplayText = string.Empty;
                ScoreDiffDisplay = string.Empty;
                HasWeapons = false;
            }).SafeFireAndForget("BattleDetail.ClearOnLeave");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _cts?.Cancel(); } catch { }
                _cts?.Dispose();
                _cts = null;
            }
            base.Dispose(disposing);
        }

        private async Task LoadAsync(PlayerSourceContext ctx, string battleId, CancellationToken ct)
        {
            IsLoading = true;
            try
            {
                var detail = await _loader.FetchBattleDetailAsync(ctx, battleId, ct).ConfigureAwait(false);
                await _uiDispatcher.InvokeAsync(() =>
                {
                    ApplyDetail(detail);
                    IsLoading = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.Error(ex, "BattleDetail", "Load failed");
                await _uiDispatcher.InvokeAsync(() => { IsLoading = false; });
            }
        }

        private void ApplyDetail(UnifiedBattleDetail? d)
        {
            ApplyPersonal(d?.Personal);
            ApplyTeam(d?.Team);
            ApplyTop5(d?.Top5);
            // 当前选中的 tab 若因数据缺失而隐藏，回退到始终存在的"个人表现"。
            if ((SelectedTab == "Team" && !HasTeam) || (SelectedTab == "Top5" && !HasTop5))
                SelectedTab = "Personal";
        }

        private void ApplyPersonal(UnifiedPersonalDetail? p)
        {
            if (p == null) return;

            // 模式名优先用详情接口自己返回的完整 mode.name；小黑盒不返回它，
            // 于是保留导航参数透传的大类文本作回退。
            if (!string.IsNullOrEmpty(p.ModeName))
                ModeType = p.ModeName!;

            BattleTime = FormatShortTime(p.BattleEndTimeMs);
            RankText = FormatRank(p.Rank);
            MapName = p.MapName;

            // 段位名 / 段位图标优先用详情接口自带的结算值（result.level / level_img）：
            // 服务端下发的段位名比本地"分数 → 段位名"映射更权威，缺失时才保留战绩页透传的导航参数。
            if (!IsPlaceholderText(p.LevelName))
            {
                RankDisplayText = p.LevelName;
                IsRankMode = true;
            }
            RankIcon = p.LevelIcon;

            // rating / rating_delta 与导航参数同源（战绩页那两个值就是从它算出来的），
            // 因此只在导航参数缺失（例如不带参数直接打开详情）时回填，
            // 避免把天选模式本地换算出的"段位内分数 + 星数"覆盖成原始总分。
            if (p.RoundRankScore > 0)
            {
                if (!ShowScoreNumber)
                {
                    ScoreNumber = p.RoundRankScore;
                    ShowScoreNumber = true;
                }
                if (string.IsNullOrEmpty(ScoreDiffDisplay))
                {
                    ScoreDiff = p.ScoreDelta;
                    ScoreDiffDisplay = FormatScoreDiff(p.ScoreDelta);
                }
            }

            PlayerAvatar = p.HeroIcon;
            PlayerName = p.RoleName;
            HeroName = p.HeroName;

            HonorTitles.Clear();
            foreach (var h in p.HonorTitles)
                HonorTitles.Add(new HonorTitleDisplay
                {
                    Icon = h.Icon,
                    Name = h.Name,
                    Desc = h.Desc,
                });

            // 4 大 core-data + 剩余 MoreStats (从 DataList 中分离前 4 个作 core，其余作展开)
            CoreData.Clear();
            MoreStats.Clear();
            var i = 0;
            foreach (var s in p.DataList)
            {
                var entry = new StatEntryDisplay { Name = s.Name, Value = s.Value };
                if (i < 4)
                    CoreData.Add(new CoreDataItem { Label = entry.Name, Value = entry.Value });
                else
                    MoreStats.Add(entry);
                i++;
            }
            HasMoreStats = MoreStats.Count > 0;
            ShowMoreStats = false; // 每次打开详情默认折叠，与网页一致

            // 详情 weapon_list：name / per(0..1) / img（"其他"这一项**没有 img 键**）/ kill_times / damage。
            Weapons.Clear();
            foreach (var w in p.Weapons)
                Weapons.Add(new WeaponDisplay
                {
                    Icon = w.Icon,
                    Name = w.Name,
                    Level = w.Level,
                    Kill = w.Kill,
                    Damage = w.Damage,
                    Percent = w.Percent,
                });
            HasWeapons = Weapons.Count > 0;

            SoulItems.Clear();
            foreach (var s in p.SoulItems)
                SoulItems.Add(new SoulItemDisplay
                {
                    Icon = s.Icon,
                    Name = s.Name,
                    Level = s.Level,
                });
        }

        /// <summary>
        /// 队伍 / Top5 在当前接口下**恒为空**：实测 <c>all_team</c> 是空数组、旧字段
        /// <c>all_player_data</c> 已不再下发（见 <c>UnifiedMapper.MapMatchDetail</c>），
        /// 所以 HasTeam / HasTop5 永远为 false，这两个 Tab 不会出现。
        /// 这里保留整条渲染链路（而非删除），一旦服务端恢复队伍数据即可直接生效；
        /// 队友的武器 / 魂玉明细在映射层就是硬编码的空数组，不作为展示依据。
        /// </summary>
        private void ApplyTeam(System.Collections.Generic.IReadOnlyList<UnifiedTeammate>? teammates)
        {
            Teammates.Clear();
            HasTeam = teammates != null && teammates.Count > 0;
            if (teammates == null) return;
            foreach (var m in teammates)
            {
                var weapons = new ObservableCollection<WeaponDisplay>();
                foreach (var w in m.Weapons)
                    weapons.Add(new WeaponDisplay
                    {
                        Icon = w.Icon,
                        Name = w.Name,
                        Level = w.Level,
                        Kill = w.Kill,
                        Damage = w.Damage,
                        Percent = w.Percent,
                    });
                var souls = new ObservableCollection<SoulItemDisplay>();
                foreach (var s in m.SoulItems)
                    souls.Add(new SoulItemDisplay
                    {
                        Icon = s.Icon,
                        Name = s.Name,
                        Level = s.Level,
                    });
                var dataList = new ObservableCollection<StatEntryDisplay>();
                foreach (var s in m.DataList)
                    dataList.Add(new StatEntryDisplay { Name = s.Name, Value = s.Value });

                Teammates.Add(new TeammateDisplay
                {
                    HeroIcon = m.HeroIcon,
                    HeroName = m.HeroName,
                    RoleName = m.RoleName,
                    IsMe = m.IsMe,
                    ArmorIcon = m.Armor?.Icon ?? string.Empty,
                    ArmorLevel = m.Armor?.Level ?? 0,
                    Weapons = weapons,
                    SoulItems = souls,
                    DataList = dataList,
                });
            }
        }

        /// <summary>前五名队伍；与 <see cref="ApplyTeam"/> 同因（<c>all_team</c> 为空）实际恒无数据。</summary>
        private void ApplyTop5(System.Collections.Generic.IReadOnlyList<UnifiedTop5Entry>? top5)
        {
            Top5Entries.Clear();
            HasTop5 = top5 != null && top5.Count > 0;
            if (top5 == null) return;
            foreach (var e in top5)
            {
                var entry = new Top5EntryDisplay { Rank = FormatRank(e.Rank) };
                foreach (var m in e.Members)
                    entry.Members.Add(new Top5MemberDisplay
                    {
                        HeroIcon = m.HeroIcon,
                        HeroName = m.HeroName,
                        RoleName = m.RoleName,
                        IsMe = m.IsMe,
                    });
                Top5Entries.Add(entry);
            }
        }

        private static string FormatRank(int rank)
        {
            if (rank <= 0) return string.Empty;
            return "#" + rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 小黑盒在文本位会用 "None" / "-" 占位（表示"本局没有这一项"），不能当有效文本展示。
        /// </summary>
        private static bool IsPlaceholderText(string? text)
            => string.IsNullOrWhiteSpace(text)
               || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase)
               || text == "-";

        /// <summary>与战绩页 FormatScoreDiff 保持同一格式：+39 → "(+39)"，-12 → "(-12)"。</summary>
        private static string FormatScoreDiff(double diff)
            => "(" + (diff >= 0 ? "+" : string.Empty) + diff + ")";

        private static string FormatShortTime(long unixMs)
        {
            if (unixMs <= 0) return string.Empty;
            try
            {
                // 后端 battleEndTime 单位为毫秒（miniProgram 原生就是毫秒；heyBox time 是秒，
                // 已在 UnifiedMapper.MapHeyBoxRecent / MapHeyBoxBattleDetail 处 * 1000 归一化）。
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().DateTime;
                return dt.ToString("MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return string.Empty; }
        }

    }

    public class StatEntryDisplay
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class CoreDataItem
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public class WeaponDisplay
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Level { get; set; }
        public int Kill { get; set; }
        public int Damage { get; set; }
        public double Percent { get; set; }

        /// <summary>
        /// 详情 weapon_list 的最后一项"其他"**没有 img 键**（键缺失，不是空串），
        /// 所以图标位要按"有没有图"分支，不能硬绑一个空来源的 Image。
        /// </summary>
        public bool HasIcon => !string.IsNullOrEmpty(Icon);
    }

    public class SoulItemDisplay
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Level { get; set; }
    }

    public class HonorTitleDisplay
    {
        public string Icon { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
    }

    public class TeammateDisplay
    {
        public string HeroIcon { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public bool IsMe { get; set; }
        public string ArmorIcon { get; set; } = string.Empty;
        public double ArmorLevel { get; set; }
        public ObservableCollection<WeaponDisplay> Weapons { get; set; } = new();
        public ObservableCollection<SoulItemDisplay> SoulItems { get; set; } = new();
        public ObservableCollection<StatEntryDisplay> DataList { get; set; } = new();
    }

    public class Top5EntryDisplay
    {
        public string Rank { get; set; } = string.Empty;
        public ObservableCollection<Top5MemberDisplay> Members { get; set; } = new();
    }

    public class Top5MemberDisplay
    {
        public string HeroIcon { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public bool IsMe { get; set; }
    }
}
