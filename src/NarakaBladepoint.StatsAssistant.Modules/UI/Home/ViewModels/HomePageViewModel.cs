using NarakaBladepoint.StatsAssistant.Framework.Core.Infrastructure;
using NarakaBladepoint.StatsAssistant.Framework.Http;
using NarakaBladepoint.StatsAssistant.Framework.Services.Abstractions;

namespace NarakaBladepoint.StatsAssistant.Modules.UI.Home.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
        private readonly HomePageVisualNavigator _homeNavigator;
        private readonly IPlayerPrefsService _playerPrefsService;
        private CancellationTokenSource? _cts;

        public HomePageViewModel(HomePageVisualNavigator homeNavigator, IPlayerPrefsService playerPrefsService)
        {
            _homeNavigator = homeNavigator;
            _playerPrefsService = playerPrefsService;
        }

        private string _playerName = string.Empty;
        public string PlayerName
        {
            get => _playerName;
            set => SetProperty(ref _playerName, value);
        }

        private int _playerLevel;
        public int PlayerLevel
        {
            get => _playerLevel;
            set => SetProperty(ref _playerLevel, value);
        }

        private string _uid = string.Empty;
        public string Uid
        {
            get => _uid;
            set => SetProperty(ref _uid, value);
        }

        private string _roleId = string.Empty;
        public string RoleId
        {
            get => _roleId;
            set => SetProperty(ref _roleId, value);
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetProperty(ref _avatarUrl, value);
        }

        private int _soloGrade;
        public int SoloGrade
        {
            get => _soloGrade;
            set => SetProperty(ref _soloGrade, value);
        }

        private int _duoGrade;
        public int DuoGrade
        {
            get => _duoGrade;
            set => SetProperty(ref _duoGrade, value);
        }

        private int _trioGrade;
        public int TrioGrade
        {
            get => _trioGrade;
            set => SetProperty(ref _trioGrade, value);
        }

        private int _currentSeasonId;
        public int CurrentSeasonId
        {
            get => _currentSeasonId;
            set => SetProperty(ref _currentSeasonId, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _isDataLoaded;
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set => SetProperty(ref _isDataLoaded, value);
        }

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);
            CancelAndDisposeCts();
            _cts = new CancellationTokenSource();
            _ = LoadUserDataAsync(_cts.Token);
        }

        protected override void OnNavigatedFromExecute(NavigationContext navigationContext)
        {
            CancelAndDisposeCts();
            ClearImageBindings();
            base.OnNavigatedFromExecute(navigationContext);
        }

        private void CancelAndDisposeCts()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            try { _cts.Dispose(); } catch (ObjectDisposedException) { }
            _cts = null;
        }

        private void ClearImageBindings()
        {
            AvatarUrl = string.Empty;
        }

        private async System.Threading.Tasks.Task LoadUserDataAsync(CancellationToken ct)
        {
            var localName = _playerPrefsService.Current.PlayerName;
            if (string.IsNullOrEmpty(localName))
            {
                IsLoading = false;
                HasError = true;
                ErrorMessage = L("Home.NoLocalData", "No local player data found. Launch NarakaBladepoint first.");
                return;
            }

            IsLoading = true;
            HasError = false;
            IsDataLoaded = false;

            try
            {
                var searchResult = await NarakaApiClient.SearchRecordAsync(localName, ct);
                if (searchResult?.Code != 200 || searchResult.Data == null)
                {
                    HasError = true;
                    ErrorMessage = searchResult?.Msg ?? L("Home.PlayerNotFound", "Player not found on server.");
                    return;
                }

                var miniRoleId = searchResult.Data.RoleIdMiniProgram;
                if (string.IsNullOrEmpty(miniRoleId))
                {
                    HasError = true;
                    ErrorMessage = L("Home.RoleIdNotAvailable", "Player role ID not available.");
                    return;
                }

                var userInfo = await NarakaApiClient.GetUserInfoAsync(miniRoleId, ct);
                if (userInfo?.Code != 200 || userInfo.Data == null)
                {
                    HasError = true;
                    ErrorMessage = userInfo?.Msg ?? L("Home.LoadUserInfoFailed", "Failed to load user info.");
                    return;
                }

                var data = userInfo.Data;
                PlayerName = data.Role?.RoleName ?? data.NickName ?? localName;
                PlayerLevel = data.Role?.RoleLevel ?? 0;
                Uid = data.Role?.Uid ?? searchResult.Data.RoleId ?? string.Empty;
                RoleId = data.Role?.RoleId ?? miniRoleId;
                AvatarUrl = data.Role?.HeadIcon ?? data.AvatarUrl ?? string.Empty;
                SoloGrade = data.SurviveSingleGrade;
                DuoGrade = data.SurviveDoubleGrade;
                TrioGrade = data.SurviveTriplexGrade;
                CurrentSeasonId = data.CurrentSeasonId;

                IsDataLoaded = true;
            }
            catch (OperationCanceledException)
            {
                // Navigation away — deliberately cancelled, not an error
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = string.Format(L("Home.NetworkError", "Network error: {0}"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
