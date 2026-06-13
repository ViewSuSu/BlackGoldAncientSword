using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    public class TeamOverlayMemberItem : ViewModelBase
    {
        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetProperty(ref _avatarUrl, value);
        }

        private string _rankName = string.Empty;
        public string RankName
        {
            get => _rankName;
            set => SetProperty(ref _rankName, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }
    }
}
