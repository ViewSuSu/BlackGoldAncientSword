using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Http.Unified;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.Stats.Services;

namespace BlackGoldAncientSword.Modules.UI.BattleDetail.ViewModels
{
    /// <summary>
    /// 对局详情浮层：拉取归一化后的 <see cref="UnifiedBattleDetail"/>，
    /// miniProgram 分支包含 personal/team/top5 三份数据；heyBox 分支仅包含 personal，
    /// team/top5 集合置空，UI 侧对应 Tab 显示空状态。
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

        private DelegateCommand<string>? _switchTabCommand;
        public DelegateCommand<string> SwitchTabCommand =>
            _switchTabCommand ??= new DelegateCommand<string>(tab => { if (!string.IsNullOrEmpty(tab)) SelectedTab = tab; });

        // === 展开更多数据 ===
        private bool _showMoreStats;
        public bool ShowMoreStats { get => _showMoreStats; set { _showMoreStats = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ExpandMoreText)); } }
        public string ExpandMoreText => ShowMoreStats ? "收起" : "展开更多数据";
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

        private string _rankText = string.Empty;
        public string RankText { get => _rankText; set { _rankText = value; RaisePropertyChanged(); } }

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
            var dataSourceCode = p.GetValue<int?>("DataSource") ?? (int)DataSource.MiniProgram;
            var dataSource = (DataSource)dataSourceCode;

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

            var sourceContext = new PlayerSourceContext(roleId!, dataSource);
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
                RankDisplayText = string.Empty;
                ScoreDiffDisplay = string.Empty;
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
                System.Diagnostics.Debug.WriteLine($"[BattleDetail] Load failed: {ex.Message}");
                await _uiDispatcher.InvokeAsync(() => { IsLoading = false; });
            }
        }

        private void ApplyDetail(UnifiedBattleDetail? d)
        {
            ApplyPersonal(d?.Personal);
            ApplyTeam(d?.Team);
            ApplyTop5(d?.Top5);
        }

        private void ApplyPersonal(UnifiedPersonalDetail? p)
        {
            if (p == null) return;

            BattleTime = FormatShortTime(p.BattleEndTimeMs);
            RankText = FormatRank(p.Rank);

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

            SoulItems.Clear();
            foreach (var s in p.SoulItems)
                SoulItems.Add(new SoulItemDisplay
                {
                    Icon = s.Icon,
                    Name = s.Name,
                    Level = s.Level,
                });
        }

        private void ApplyTeam(System.Collections.Generic.IReadOnlyList<UnifiedTeammate>? teammates)
        {
            Teammates.Clear();
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

        private void ApplyTop5(System.Collections.Generic.IReadOnlyList<UnifiedTop5Entry>? top5)
        {
            Top5Entries.Clear();
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
