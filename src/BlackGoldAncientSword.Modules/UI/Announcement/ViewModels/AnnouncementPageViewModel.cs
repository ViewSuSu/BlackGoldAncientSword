using System;
using System.Collections.ObjectModel;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using BlackGoldAncientSword.Framework.Core.Extensions;
using BlackGoldAncientSword.Framework.Services.Abstractions;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.Announcement.ViewModels
{
    public class AnnouncementPageViewModel : ViewModelBase
    {
        private readonly IGitHubReleaseService _releaseService;

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

        public AnnouncementPageViewModel(IGitHubReleaseService releaseService)
        {
            _releaseService = releaseService;
            IsLoading = true;
            LoadReleasesAsync().SafeFireAndForget("Announcement.LoadReleases");
        }

        private async System.Threading.Tasks.Task LoadReleasesAsync()
        {
            try
            {
                var releases = await _releaseService.GetReleasesAsync();
                await Application.Current.Dispatcher.InvokeAsync(() =>
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
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsLoading = false;
                });
            }
        }

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                var rgn = regionManager.Regions[GlobalConstant.AnnouncementRegion];
                rgn.RemoveAll();
            });

        private DelegateCommand? _confirmCommand;
        public DelegateCommand ConfirmCommand =>
            _confirmCommand ??= new DelegateCommand(() =>
            {
                var rgn = regionManager.Regions[GlobalConstant.AnnouncementRegion];
                rgn.RemoveAll();
            });
    }

    public class UpdateHistoryItem
    {
        public string Version { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }
}