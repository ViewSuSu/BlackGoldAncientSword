using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;

namespace BlackGoldAncientSword.Framework.UI.Controls
{
    public class TeamOverlayMemberItem : ViewModelBase
    {
        private string _userName = string.Empty;
        public string UserName
        {
            get => _userName;
            set
            {
                if (_userName == value) return;
                _userName = value;
                RaisePropertyChanged(nameof(UserName));
            }
        }

        private string _avatarUrl = string.Empty;
        public string AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                if (_avatarUrl == value) return;
                _avatarUrl = value;
                RaisePropertyChanged(nameof(AvatarUrl));
            }
        }

        private string _rankName = string.Empty;
        public string RankName
        {
            get => _rankName;
            set
            {
                if (_rankName == value) return;
                _rankName = value;
                RaisePropertyChanged(nameof(RankName));
            }
        }

        private string _rankIcon = string.Empty;
        public string RankIcon
        {
            get => _rankIcon;
            set
            {
                if (_rankIcon == value) return;
                _rankIcon = value;
                RaisePropertyChanged(nameof(RankIcon));
            }
        }

        private string _pageRankName = string.Empty;
        public string PageRankName
        {
            get => _pageRankName;
            set
            {
                if (_pageRankName == value) return;
                _pageRankName = value;
                RaisePropertyChanged(nameof(PageRankName));
            }
        }

        private int _pageStarCount;
        public int PageStarCount
        {
            get => _pageStarCount;
            set
            {
                if (_pageStarCount == value) return;
                _pageStarCount = value;
                RaisePropertyChanged(nameof(PageStarCount));
            }
        }

        private bool _pageHasStars;
        public bool PageHasStars
        {
            get => _pageHasStars;
            set
            {
                if (_pageHasStars == value) return;
                _pageHasStars = value;
                RaisePropertyChanged(nameof(PageHasStars));
            }
        }

        private double _rankTierScore;
        public double RankTierScore
        {
            get => _rankTierScore;
            set
            {
                if (_rankTierScore == value) return;
                _rankTierScore = value;
                RaisePropertyChanged(nameof(RankTierScore));
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                RaisePropertyChanged(nameof(IsLoading));
            }
        }
    }
}