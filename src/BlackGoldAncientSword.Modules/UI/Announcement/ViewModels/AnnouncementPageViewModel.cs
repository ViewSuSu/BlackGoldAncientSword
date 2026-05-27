using System.Collections.ObjectModel;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.Announcement.ViewModels
{
    public class AnnouncementPageViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;

        public ObservableCollection<UpdateHistoryItem> UpdateHistory { get; } = new()
        {
            new UpdateHistoryItem { Version = "v1.0.0", Title = "初始版本", Detail = "支持永劫无间战绩查询、搜索玩家、数据保存等功能。" }
        };

        public string Notice => "感谢使用永劫无间战绩查询助手！如有问题或建议，欢迎反馈。";

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