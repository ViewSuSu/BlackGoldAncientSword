using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.Announcement.ViewModels
{
    public class AnnouncementPageViewModel : ViewModelBase
    {
        private static string L(string key, string fallback) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

        private readonly IRegionManager _regionManager;

        public ObservableCollection<UpdateHistoryItem> UpdateHistory { get; } = new()
        {
            new UpdateHistoryItem { Version = "v1.0.0", Title = L("Announcement.InitialVersion", "初始版本"), Detail = L("Announcement.InitialDetail", "支持永劫无间战绩查询、搜索玩家、数据保存等功能。") }
        };

        public string Notice => L("Announcement.ThanksMessage", "感谢使用永劫无间战绩查询助手！如有问题或建议，欢迎反馈。");

        public AnnouncementPageViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.AnnouncementRegion];
                region.RemoveAll();
            });

        private DelegateCommand? _confirmCommand;
        public DelegateCommand ConfirmCommand =>
            _confirmCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.AnnouncementRegion];
                region.RemoveAll();
            });
    }

    public class UpdateHistoryItem
    {
        public string Version { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }
}