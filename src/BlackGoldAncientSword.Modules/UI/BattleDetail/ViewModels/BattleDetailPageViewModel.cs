using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.Stats.Services;

namespace BlackGoldAncientSword.Modules.UI.BattleDetail.ViewModels
{
    /// <summary>
    /// 对局详情浮层：并行拉 personal / team / top5 三份数据。Tab 切换在 UI 侧完成。
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
            LoadAsync(roleId!, battleId!, _cts.Token).SafeFireAndForget("BattleDetail.Load");
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

        private async Task LoadAsync(string roleId, string battleId, CancellationToken ct)
        {
            IsLoading = true;
            try
            {
                var detailTask = _loader.FetchBattleDetailAsync(roleId, battleId, ct);
                var teamTask = _loader.FetchTeamBattleDetailAsync(roleId, battleId, ct);
                var top5Task = InvokeAsync(() => NarakaApiClient.GetTop5BattleDetailAsync(roleId, battleId, ct));
                await Task.WhenAll(detailTask, teamTask, top5Task).ConfigureAwait(false);

                var detail = detailTask.Result?.Data;
                var team = teamTask.Result?.Data;
                var top5 = top5Task.Result?.Data;
                await _uiDispatcher.InvokeAsync(() =>
                {
                    ApplyDetail(detail);
                    ApplyTeam(team);
                    ApplyTop5(top5);
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

        private static async Task<T?> InvokeAsync<T>(Func<Task<T>> call) where T : class
        {
            try { return await call().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        private void ApplyDetail(BattleDetailData? d)
        {
            if (d == null) return;

            // 顶部信息栏（ModeType / TeamSizeGlyph / RankDisplayText 已由 NavigationParameters 填好，
            // 这里只补 API 才有的 BattleTime + Rank #）
            BattleTime = FormatShortTime(d.BattleEndTime);
            RankText = FormatRank(d.Rank);

            // Personal header
            PlayerAvatar = d.Hero?.HeroIcon ?? string.Empty;
            PlayerName = d.Role?.RoleName ?? string.Empty;
            HeroName = d.Hero?.HeroName ?? string.Empty;

            HonorTitles.Clear();
            if (d.HonorTitles != null)
                foreach (var h in d.HonorTitles)
                    HonorTitles.Add(new HonorTitleDisplay
                    {
                        Icon = h.HonorIcon ?? string.Empty,
                        Name = h.HonorName ?? string.Empty,
                        Desc = h.HonorDesc ?? string.Empty,
                    });

            // 4 大 core-data + 剩余 MoreStats (从 DataList 中分离前 4 个作 core，其余作展开)
            CoreData.Clear();
            MoreStats.Clear();
            if (d.DataList != null)
            {
                var i = 0;
                foreach (var s in d.DataList)
                {
                    var entry = new StatEntryDisplay { Name = s.Name ?? string.Empty, Value = s.Value ?? string.Empty };
                    if (i < 4)
                        CoreData.Add(new CoreDataItem { Label = entry.Name, Value = entry.Value });
                    else
                        MoreStats.Add(entry);
                    i++;
                }
            }

            Weapons.Clear();
            if (d.Weapons != null)
                foreach (var w in d.Weapons)
                    Weapons.Add(new WeaponDisplay
                    {
                        Icon = w.WeaponIcon ?? string.Empty,
                        Name = w.WeaponName ?? string.Empty,
                        Level = w.WeaponLevel ?? 0,
                        Kill = (int)(w.Kill ?? 0),
                        Damage = (int)(w.Damage ?? 0),
                        Percent = (double)(w.Percent ?? 0),
                    });

            SoulItems.Clear();
            if (d.SoulItems != null)
                foreach (var s in d.SoulItems)
                    SoulItems.Add(new SoulItemDisplay
                    {
                        Icon = s.SoulItemIcon ?? string.Empty,
                        Name = s.SoulItemName ?? string.Empty,
                        Level = s.SoulItemLevel ?? 0,
                    });
        }

        private void ApplyTeam(TeamBattleDetailData? t)
        {
            Teammates.Clear();
            if (t?.Teammates == null) return;
            foreach (var m in t.Teammates)
            {
                Teammates.Add(new TeammateDisplay
                {
                    HeroIcon = m.Hero?.HeroIcon ?? string.Empty,
                    HeroName = m.Hero?.HeroName ?? string.Empty,
                    RoleName = m.Role?.RoleName ?? string.Empty,
                    IsMe = m.IsMe ?? false,
                    ArmorIcon = m.Armor?.ArmorIcon ?? string.Empty,
                    ArmorLevel = m.Armor?.ArmorLevel ?? 0,
                    Weapons = new ObservableCollection<WeaponDisplay>(BuildWeapons(m.Weapons)),
                    SoulItems = new ObservableCollection<SoulItemDisplay>(BuildSouls(m.SoulItems)),
                    DataList = new ObservableCollection<StatEntryDisplay>(BuildStats(m.DataList)),
                });
            }
        }

        private void ApplyTop5(Top5BattleDetailData? t)
        {
            Top5Entries.Clear();
            if (t?.Top5 == null) return;
            foreach (var e in t.Top5)
            {
                var entry = new Top5EntryDisplay { Rank = FormatRank(e.Rank) };
                if (e.Members != null)
                    foreach (var m in e.Members)
                        entry.Members.Add(new Top5MemberDisplay
                        {
                            HeroIcon = m.Hero?.HeroIcon ?? string.Empty,
                            HeroName = m.Hero?.HeroName ?? string.Empty,
                            RoleName = m.Role?.RoleName ?? string.Empty,
                            IsMe = m.IsMe ?? false,
                        });
                Top5Entries.Add(entry);
            }
        }

        // === 帮助方法 ===
        private static System.Collections.Generic.IEnumerable<WeaponDisplay> BuildWeapons(System.Collections.Generic.List<WeaponInfo>? list)
        {
            if (list == null) yield break;
            foreach (var w in list)
                yield return new WeaponDisplay
                {
                    Icon = w.WeaponIcon ?? string.Empty,
                    Name = w.WeaponName ?? string.Empty,
                    Level = w.WeaponLevel ?? 0,
                    Kill = (int)(w.Kill ?? 0),
                    Damage = (int)(w.Damage ?? 0),
                    Percent = (double)(w.Percent ?? 0),
                };
        }

        private static System.Collections.Generic.IEnumerable<SoulItemDisplay> BuildSouls(System.Collections.Generic.List<SoulItemInfo>? list)
        {
            if (list == null) yield break;
            foreach (var s in list)
                yield return new SoulItemDisplay
                {
                    Icon = s.SoulItemIcon ?? string.Empty,
                    Name = s.SoulItemName ?? string.Empty,
                    Level = s.SoulItemLevel ?? 0,
                };
        }

        private static System.Collections.Generic.IEnumerable<StatEntryDisplay> BuildStats(System.Collections.Generic.List<StatItem>? list)
        {
            if (list == null) yield break;
            foreach (var s in list)
                yield return new StatEntryDisplay { Name = s.Name ?? string.Empty, Value = s.Value ?? string.Empty };
        }

        private static string FormatRank(double? rank)
        {
            if (rank == null || rank <= 0) return string.Empty;
            return "#" + ((int)rank.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatShortTime(long? unixMs)
        {
            if (unixMs == null || unixMs <= 0) return string.Empty;
            try
            {
                // 后端 battleEndTime 单位为毫秒（如 1782490539000）。
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value).ToLocalTime().DateTime;
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
