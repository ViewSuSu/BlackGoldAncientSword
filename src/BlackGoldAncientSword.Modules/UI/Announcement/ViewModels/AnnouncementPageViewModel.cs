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
        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

        private readonly IGitHubReleaseService _releaseService;

        public ObservableCollection<UpdateHistoryItem> UpdateHistory { get; } = new();

        private string _notice = string.Empty;
        public string Notice
        {
            get => _notice;
            set
            {
                _notice = value;
                RaisePropertyChanged();
            }
        }

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
            Notice = L("Announcement.Loading", "正在加载更新历史...");
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
                    if (releases.Count > 0)
                    {
                        foreach (var r in releases)
                        {
                            UpdateHistory.Add(new UpdateHistoryItem
                            {
                                Version = r.TagName,
                                Title = string.IsNullOrEmpty(r.Name) ? r.TagName : r.Name,
                                Detail = r.Body,
                                Url = r.HtmlUrl
                            });
                        }
                        Notice = releases[0].Body;
                    }
                    else
                    {
                        Notice = L("Announcement.NoData", "暂无更新历史。");
                    }
                    IsLoading = false;
                });
            }
            catch (Exception)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Notice = L("Announcement.LoadFailed", "加载更新历史失败，请检查网络连接。");
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
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}