using System.Collections.ObjectModel;
using System.Diagnostics;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Generated;
using BlackGoldAncientSword.GameMonitor.Models;
using BlackGoldAncientSword.GameMonitor.Services.Abstractions;
using BlackGoldAncientSword.Modules.UI.TeamInfo.Services;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels
{
    public class TeamInfoPageViewModel : ViewModelBase
    {
        private readonly IGameStatusMonitor _gameStatusMonitor;
        private readonly ITeamInfoOcrService _teamInfoOcrService;
        private CancellationTokenSource? _ocrLoopCts;
        private bool _isOcrRunning;
        private readonly object _ocrLock = new();

        public TeamInfoPageViewModel(
            IGameStatusMonitor gameStatusMonitor,
            ITeamInfoOcrService teamInfoOcrService)
        {
            _gameStatusMonitor = gameStatusMonitor;
            _teamInfoOcrService = teamInfoOcrService;
            TeamMembers = new ObservableCollection<TeamMemberInfo>();
            _gameStatusMonitor.GameStatusRecognized += OnGameStatusRecognized;
        }

        public ObservableCollection<TeamMemberInfo> TeamMembers { get; }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _statusText = "等待游戏进入英雄选择...";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private async void OnGameStatusRecognized(object? sender, GameStatusChangedEventArgs args)
        {
            switch (args.Status)
            {
                case GameStatus.HeroSelection:
                    StatusText = "英雄选择中，正在识别队友...";
                    StartOcrLoop();
                    break;
                case GameStatus.InGame:
                    StatusText = "进入对局，队伍信息已锁定";
                    StopOcrLoop();
                    break;
                case GameStatus.BattleEnded:
                case GameStatus.Unknown:
                    StopOcrLoop();
                    StatusText = "等待游戏进入英雄选择...";
                    TeamMembers.Clear();
                    break;
            }
        }

        private void StartOcrLoop()
        {
            lock (_ocrLock)
            {
                if (_isOcrRunning) return;
                _isOcrRunning = true;
                CancelAndDispose(ref _ocrLoopCts);
                _ocrLoopCts = new CancellationTokenSource();
            }
            var ct = _ocrLoopCts!.Token;
            _ = OcrLoopAsync(ct);
        }

        private void StopOcrLoop()
        {
            lock (_ocrLock)
            {
                if (!_isOcrRunning) return;
                _isOcrRunning = false;
                CancelAndDispose(ref _ocrLoopCts);
            }
        }

        private async Task OcrLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var names = await _teamInfoOcrService.RecognizeTeamMembersAsync(ct);
                    if (names.Length > 0 && !ct.IsCancellationRequested)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await UpdateTeamMembersAsync(names, ct);
                        });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TeamInfo] OCR loop error: {ex.Message}");
                }

                try { await Task.Delay(1500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task UpdateTeamMembersAsync(string[] names, CancellationToken ct)
        {
            var existingNames = TeamMembers.Select(m => m.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newNames = names.Where(n => !existingNames.Contains(n)).ToArray();

            foreach (var name in newNames)
            {
                ct.ThrowIfCancellationRequested();
                var member = new TeamMemberInfo { UserName = name, IsLoading = true };
                TeamMembers.Add(member);
                _ = LoadMemberDataAsync(member, ct);
            }

            IsLoading = TeamMembers.Any(m => m.IsLoading);
        }

        private async Task LoadMemberDataAsync(TeamMemberInfo member, CancellationToken ct)
        {
            try
            {
                var search = await NarakaApiClient.SearchRecordAsync(member.UserName, ct);
                if (search?.Data == null || string.IsNullOrEmpty(search.Data.RoleIdSimple))
                {
                    member.StatusText = "未找到该玩家";
                    member.IsLoading = false;
                    return;
                }

                var roleId = search.Data.RoleIdSimple;
                var userInfo = await NarakaApiClient.GetUserInfoAsync(roleId, ct);
                if (userInfo?.Code == 200 && userInfo.Data != null)
                {
                    var d = userInfo.Data;
                    member.UserName = d.Role?.RoleName ?? d.NickName ?? member.UserName;
                    member.Level = $"Lv.{d.Role?.RoleLevel ?? 0}";
                    member.UID = d.Role?.Uid ?? string.Empty;
                    member.AvatarUrl = d.Role?.HeadIcon ?? string.Empty;
                    member.StatusText = string.Empty;
                }
                else
                {
                    member.StatusText = "加载失败";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeamInfo] Load member error: {ex.Message}");
                member.StatusText = "加载出错";
            }
            finally
            {
                member.IsLoading = false;
                IsLoading = TeamMembers.Any(m => m.IsLoading);
            }
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            cts.Dispose();
            cts = null;
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            StopOcrLoop();
            base.OnNavigatedFromExecute(navigationContext);
        }
    }

    public class TeamMemberInfo : ViewModelBase
    {
        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _uid = string.Empty;
        public string UID
        {
            get => _uid;
            set => SetProperty(ref _uid, value);
        }

        private string _level = string.Empty;
        public string Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetProperty(ref _avatarUrl, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
    }
}
