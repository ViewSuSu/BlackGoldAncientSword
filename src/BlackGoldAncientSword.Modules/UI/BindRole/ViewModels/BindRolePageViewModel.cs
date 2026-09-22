using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Events;
using BlackGoldAncientSword.Framework.Core.Infrastructure;
using BlackGoldAncientSword.Framework.Http;
using BlackGoldAncientSword.Framework.Http.Heybox;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.BindRole.ViewModels
{
    /// <summary>
    /// 绑定角色弹窗：对齐网页端的绑定引导（输入游戏昵称 + 选服务器 → 绑定 → 处理中轮询）。
    /// <para>
    /// 打开时先查「当前账号已绑定的角色」：查到 → 显示已绑定视图（头像 / 昵称 / 一键看战绩 / 换绑）；
    /// 未绑定或查询失败 → 显示绑定输入界面。绑定流程本身在 <see cref="HeyboxRoleBinder"/>。
    /// </para>
    /// </summary>
    public class BindRolePageViewModel : ViewModelBase
    {
        /// <summary>服务器取值与顺序对齐网页端。</summary>
        private static readonly int[] ServerIds = { 163, 164, 166, 167, 168 };

        /// <summary>「查看战绩」的防抖窗口：2 秒内只认第一次点击。</summary>
        private const int ViewStatsDebounceMs = 2000;

        private readonly HeyboxRoleBinder _binder;
        private readonly ITipMessageService _tipMessage;
        private readonly ILocalizedTextProvider _localizedText;
        private readonly IMainContentNavigationService _navigation;
        private readonly SearchDebounceGate _viewStatsDebounce = new(ViewStatsDebounceMs);

        public BindRolePageViewModel(
            HeyboxRoleBinder binder,
            ITipMessageService tipMessage,
            ILocalizedTextProvider localizedText,
            IMainContentNavigationService navigation)
        {
            _binder = binder;
            _tipMessage = tipMessage;
            _localizedText = localizedText;
            _navigation = navigation;

            Servers = new ObservableCollection<BindRoleServerOption>(
                ServerIds.Select(id => new BindRoleServerOption(id, L($"BindRole.Server.{id}", FallbackServerName(id)))));

            // 默认国服，对齐网页端。
            _selectedServer = Servers[0];

            _bindCommand = new DelegateCommand(() => _ = BindAsync())
                .ObservesCanExecute(() => CanBind);
        }

        public ObservableCollection<BindRoleServerOption> Servers { get; }

        private BindRoleServerOption _selectedServer;
        public BindRoleServerOption SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (_selectedServer == value) return;
                _selectedServer = value;
                RaisePropertyChanged(nameof(SelectedServer));
            }
        }

        // === 已绑定角色（打开弹窗时查询）===

        private bool _isCheckingBoundRole;
        /// <summary>正在查账号已绑定的角色。查询期间主体区显示加载提示。</summary>
        public bool IsCheckingBoundRole
        {
            get => _isCheckingBoundRole;
            set
            {
                if (_isCheckingBoundRole == value) return;
                _isCheckingBoundRole = value;
                RaisePropertyChanged(nameof(IsCheckingBoundRole));
                RaisePropertyChanged(nameof(ShowBindForm));
                RaisePropertyChanged(nameof(ShowBoundRoleView));
            }
        }

        private BoundRoleInfo? _boundRole;

        /// <summary>点了「绑定其他角色」：即使查到已绑定也强制显示输入界面。</summary>
        private bool _isRebinding;

        /// <summary>是否显示绑定输入界面（未绑定 / 换绑 / 查询失败时）。</summary>
        public bool ShowBindForm => !IsCheckingBoundRole && (_boundRole is null || _isRebinding);

        /// <summary>是否显示「已绑定角色」视图。</summary>
        public bool ShowBoundRoleView => !IsCheckingBoundRole && _boundRole is not null && !_isRebinding;

        public string BoundRoleName => _boundRole?.Name ?? string.Empty;

        public string BoundRoleAvatar => _boundRole?.Avatar ?? string.Empty;

        /// <summary>已绑定角色的副标题："LV.439 · 国服"（等级没拿到就只剩服务器名）。</summary>
        public string BoundRoleMeta
        {
            get
            {
                if (_boundRole is not { } role) return string.Empty;

                var parts = new List<string>(2);
                if (role.Level > 0) parts.Add($"LV.{(int)role.Level}");

                var serverName = ResolveServerName(role.Server, role.ServerDesc);
                if (!string.IsNullOrEmpty(serverName)) parts.Add(serverName);

                return string.Join(" · ", parts);
            }
        }

        private DelegateCommand? _viewBoundRoleStatsCommand;
        /// <summary>
        /// 已绑定视图的「查看战绩」：关弹窗 → 带着角色快照跳战绩页直接渲染
        /// （与队伍卡片「详情」同一条快照跳转路径）。
        /// </summary>
        public DelegateCommand ViewBoundRoleStatsCommand =>
            _viewBoundRoleStatsCommand ??= new DelegateCommand(() =>
            {
                if (_boundRole is not { } role) return;
                if (!_viewStatsDebounce.TryEnter())
                {
                    _tipMessage.ShowError(L("Search.TooFast", "点击过快请稍后重试"));
                    return;
                }

                Dismiss();
                _navigation.NavigateTo(PageNames.StatsPage, new NavigationParameters
                {
                    { NavigationParameterKeys.TargetPlayerName, role.Name },
                    { NavigationParameterKeys.TargetRoleId, role.RoleId },
                    { NavigationParameterKeys.TargetServer, role.Server },
                    { NavigationParameterKeys.TargetAvatar, role.Avatar },
                    { NavigationParameterKeys.TargetLevel, role.Level > 0 ? $"LV.{(int)role.Level}" : string.Empty },
                });
            });

        private DelegateCommand? _rebindCommand;
        /// <summary>已绑定视图的「绑定其他角色」：切到输入界面（搜索入口带过来的昵称预填保留）。</summary>
        public DelegateCommand RebindCommand =>
            _rebindCommand ??= new DelegateCommand(() =>
            {
                _isRebinding = true;
                StatusText = string.Empty;
                RaisePropertyChanged(nameof(ShowBindForm));
                RaisePropertyChanged(nameof(ShowBoundRoleView));
            });

        // === 绑定表单 ===

        private string _gameId = string.Empty;
        public string GameId
        {
            get => _gameId;
            set
            {
                if (_gameId == value) return;
                _gameId = value;
                RaisePropertyChanged(nameof(GameId));
                RaisePropertyChanged(nameof(CanBind));
            }
        }

        private bool _isBinding;
        public bool IsBinding
        {
            get => _isBinding;
            set
            {
                if (_isBinding == value) return;
                _isBinding = value;
                RaisePropertyChanged(nameof(IsBinding));
                RaisePropertyChanged(nameof(CanBind));
                RaisePropertyChanged(nameof(BindButtonText));
            }
        }

        /// <summary>绑定按钮文案：绑定中切成「绑定中…」，其余显示「绑定角色」。</summary>
        public string BindButtonText => IsBinding
            ? L("BindRole.Binding", "绑定中…")
            : L("BindRole.Confirm", "绑定角色");

        private string _statusText = string.Empty;
        /// <summary>失败提示（后端 msg 或默认文案）。空串时整个提示区隐藏。</summary>
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value) return;
                _statusText = value;
                RaisePropertyChanged(nameof(StatusText));
                RaisePropertyChanged(nameof(HasStatus));
            }
        }

        /// <summary>是否有失败提示可展示（控制提示区显隐，避免空 TextBlock 占位）。</summary>
        public bool HasStatus => !string.IsNullOrEmpty(_statusText);

        private bool CanBind => !IsBinding && !string.IsNullOrWhiteSpace(GameId);

        private readonly DelegateCommand _bindCommand;
        public DelegateCommand BindCommand => _bindCommand;

        protected override void OnNavigatedToExecute(NavigationContext navigationContext)
        {
            base.OnNavigatedToExecute(navigationContext);

            // 每次打开清掉上一次的失败提示；绑定中的状态保留——关掉弹窗不中断绑定，
            // 重新打开应当看到「绑定中…」而不是一个看似可再点的按钮。
            if (!IsBinding) StatusText = string.Empty;

            // 战绩页入口把搜索框里的昵称带过来预填（用户搜不到的那个名字，多半就是他要绑的）；
            // 用户卡片入口不带参数 → 清空，避免残留上一次的输入。
            var prefill = navigationContext.Parameters.GetValue<string>(NavigationParameterKeys.BindRoleGameId);
            if (!IsBinding)
                GameId = string.IsNullOrWhiteSpace(prefill) ? string.Empty : prefill.Trim();

            // 先查账号已绑定的角色：查到显示已绑定视图，未绑定/查不到显示输入界面。
            if (!IsBinding)
            {
                _isRebinding = false;
                IsCheckingBoundRole = true;
                _ = CheckBoundRoleAsync();
            }
        }

        private async System.Threading.Tasks.Task CheckBoundRoleAsync()
        {
            IsCheckingBoundRole = true;
            try
            {
                SetBoundRole(await _binder.GetBoundRoleAsync(System.Threading.CancellationToken.None));
            }
            catch (Exception ex)
            {
                // 查询失败静默退回输入界面：查不到不该挡用户自己输昵称绑定。
                AppLog.Error(ex, $"{nameof(BindRolePageViewModel)}.{nameof(CheckBoundRoleAsync)}");
                SetBoundRole(null);
            }
            finally
            {
                IsCheckingBoundRole = false;
            }
        }

        private void SetBoundRole(BoundRoleInfo? role)
        {
            _boundRole = role;
            RaisePropertyChanged(nameof(BoundRoleName));
            RaisePropertyChanged(nameof(BoundRoleAvatar));
            RaisePropertyChanged(nameof(BoundRoleMeta));
            RaisePropertyChanged(nameof(ShowBindForm));
            RaisePropertyChanged(nameof(ShowBoundRoleView));
        }

        private async System.Threading.Tasks.Task BindAsync()
        {
            if (IsBinding) return;

            var gameId = GameId.Trim();
            if (string.IsNullOrEmpty(gameId))
            {
                StatusText = L("BindRole.EmptyName", "请先输入游戏昵称");
                return;
            }

            IsBinding = true;
            StatusText = string.Empty;
            try
            {
                var outcome = await _binder.BindAsync(gameId, SelectedServer?.Id ?? ServerIds[0], System.Threading.CancellationToken.None);
                if (!outcome.Success)
                {
                    StatusText = outcome.ErrorMessage ?? L("BindRole.Failed", FallbackFailedText);
                    return;
                }

                // 对齐网页端：绑定成功后立刻**不带 role_id** 重拉一次主页数据，服务端按绑定关系返回
                // 刚绑定的角色（网页端绑定成功后重拉的 getData() 就是这条）。把快照一并广播，
                // 战绩页据此直接渲染，不再按昵称重搜一轮（搜索接口只吃昵称，刚绑定的角色有搜不到的风险）。
                // 查询失败（返回 null）不影响"绑定成功"的判定，事件退回带昵称的兜底路径。
                var boundRole = await _binder.GetBoundRoleAsync(System.Threading.CancellationToken.None);

                _tipMessage.ShowInfo(L("BindRole.Success", "绑定成功"));
                eventAggregator.GetEvent<RoleBindSucceededEvent>()
                    .Publish(new RoleBindSucceededEventArgs(gameId, boundRole));
                Dismiss();
            }
            catch (NarakaApiException ex)
            {
                // 后端业务失败：msg 原样展示；为空时退回默认文案（对齐网页端的失败文案分支）。
                StatusText = string.IsNullOrEmpty(ex.Msg) ? L("BindRole.Failed", FallbackFailedText) : ex.Msg!;
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(BindRolePageViewModel)}.{nameof(BindAsync)}");
                StatusText = L("BindRole.Failed", FallbackFailedText);
            }
            finally
            {
                IsBinding = false;
            }
        }

        private void Dismiss()
        {
            try
            {
                regionManager.Regions[GlobalConstant.BindRoleRegion].RemoveAll();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, $"{nameof(BindRolePageViewModel)}.{Dismiss}", "dismiss bind role overlay failed");
            }
        }

        private string L(string key, string fallback) => _localizedText.Get(key, fallback);

        /// <summary>服务器展示名：优先本地化资源（按数字 id），解析不出来时退回服务端下发的中文描述。</summary>
        private string ResolveServerName(string server, string serverDesc)
        {
            if (int.TryParse(server, out var id))
            {
                var name = L($"BindRole.Server.{id}", string.Empty);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return serverDesc;
        }

        private static string FallbackServerName(int id) => id switch
        {
            163 => "国服",
            164 => "北美",
            166 => "欧服",
            167 => "亚服",
            168 => "东南亚",
            _ => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        /// <summary>默认失败文案与本地化资源一致（对齐网页端的兜底文案）。</summary>
        private const string FallbackFailedText = "绑定失败：\n1. 英文请区分大小写\n2. 队列过长请稍后再试";
    }

    /// <summary>服务器下拉项。</summary>
    public class BindRoleServerOption
    {
        public BindRoleServerOption(int id, string label)
        {
            Id = id;
            Label = label;
        }

        public int Id { get; }

        public string Label { get; }
    }
}
