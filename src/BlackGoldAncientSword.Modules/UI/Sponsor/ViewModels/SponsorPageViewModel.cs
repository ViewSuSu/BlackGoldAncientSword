using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackGoldAncientSword.Framework.Core.Bases.ViewModels;
using BlackGoldAncientSword.Framework.Core.Consts;
using Prism.Regions;

namespace BlackGoldAncientSword.Modules.UI.Sponsor.ViewModels
{
    public class SponsorPageViewModel : ViewModelBase
    {
        private readonly IRegionManager _regionManager;

        private const string WeChatQrPath = "/BlackGoldAncientSword.Resources;component/Images/wechat_sponsor_qrcode.jpg";
        private const string AlipayQrPath = "/BlackGoldAncientSword.Resources;component/Images/alipay_sponsor_qrcode.jpg";

        public SponsorPageViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        private bool _isWeChatSelected = true;
        public bool IsWeChatSelected
        {
            get => _isWeChatSelected;
            set
            {
                if (_isWeChatSelected == value) return;
                _isWeChatSelected = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsAlipaySelected));
                if (_qrCodeVisible)
                    UpdateQrCodeSource();
            }
        }

        public bool IsAlipaySelected
        {
            get => !_isWeChatSelected;
            set
            {
                if (IsAlipaySelected == value) return;
                _isWeChatSelected = !value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsWeChatSelected));
                if (_qrCodeVisible)
                    UpdateQrCodeSource();
            }
        }

        private bool _qrCodeVisible;
        public bool QrCodeVisible
        {
            get => _qrCodeVisible;
            set
            {
                if (_qrCodeVisible == value) return;
                _qrCodeVisible = value;
                RaisePropertyChanged();
                if (value)
                    UpdateQrCodeSource();
            }
        }

        private ImageSource? _qrCodeSource;
        public ImageSource? QrCodeSource
        {
            get => _qrCodeSource;
            set
            {
                _qrCodeSource = value;
                RaisePropertyChanged();
            }
        }

        private DelegateCommand? _selectWeChatCommand;
        public DelegateCommand SelectWeChatCommand =>
            _selectWeChatCommand ??= new DelegateCommand(() => IsWeChatSelected = true);

        private DelegateCommand? _selectAlipayCommand;
        public DelegateCommand SelectAlipayCommand =>
            _selectAlipayCommand ??= new DelegateCommand(() => IsAlipaySelected = true);

        private DelegateCommand? _showQrCodeCommand;
        public DelegateCommand ShowQrCodeCommand =>
            _showQrCodeCommand ??= new DelegateCommand(() => QrCodeVisible = true);

        private DelegateCommand? _dismissCommand;
        public DelegateCommand DismissCommand =>
            _dismissCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.SponsorRegion];
                region.RemoveAll();
            });

        private DelegateCommand? _confirmCommand;
        public DelegateCommand ConfirmCommand =>
            _confirmCommand ??= new DelegateCommand(() =>
            {
                var region = _regionManager.Regions[GlobalConstant.SponsorRegion];
                region.RemoveAll();
            });

        private void UpdateQrCodeSource()
        {
            var path = _isWeChatSelected ? WeChatQrPath : AlipayQrPath;
            QrCodeSource = new BitmapImage(new Uri(path, UriKind.Relative));
        }
    }
}
