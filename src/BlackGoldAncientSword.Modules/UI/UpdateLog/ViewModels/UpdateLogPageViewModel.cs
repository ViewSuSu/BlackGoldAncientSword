using System;
using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.UpdateLog.ViewModels
{
    public class UpdateLogPageViewModel : ViewModelBase
    {
        private readonly IGiteeReleaseService _releaseService;
        private readonly IUIDispatcher _uiDispatcher;

        public ObservableCollection<UpdateHistoryItem> UpdateHistory { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                RaisePropertyChanged();
            }
        }

        public UpdateLogPageViewModel(IGiteeReleaseService releaseService, IUIDispatcher uiDispatcher)
        {
            _releaseService = releaseService;
            _uiDispatcher = uiDispatcher;
            IsLoading = true;
            LoadReleasesAsync().SafeFireAndForget($"{nameof(UpdateLogPageViewModel)}.LoadReleases");
        }

        private async System.Threading.Tasks.Task LoadReleasesAsync()
        {
            try
            {
                var releases = await _releaseService.GetReleasesAsync();
                await _uiDispatcher.InvokeAsync(() =>
                {
                    UpdateHistory.Clear();
                    foreach (var r in releases)
                    {
                        UpdateHistory.Add(new UpdateHistoryItem
                        {
                            Version = r.TagName,
                            Detail = r.Body
                        });
                    }
                    IsLoading = false;
                });
            }
            catch (Exception)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    IsLoading = false;
                });
            }
        }

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                var rgn = regionManager.Regions[GlobalConstant.UpdateLogRegion];
                rgn.RemoveAll();
            });

        private DelegateCommand? _confirmCommand;
        public DelegateCommand ConfirmCommand =>
            _confirmCommand ??= new DelegateCommand(() =>
            {
                var rgn = regionManager.Regions[GlobalConstant.UpdateLogRegion];
                rgn.RemoveAll();
            });
    }

    public class UpdateHistoryItem
    {
        public string Version { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }
}
