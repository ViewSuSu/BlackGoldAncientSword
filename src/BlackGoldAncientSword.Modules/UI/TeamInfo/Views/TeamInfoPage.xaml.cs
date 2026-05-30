using System.ComponentModel;
using System.Windows;
using BlackGoldAncientSword.Framework.Core.Bases.Views;
using BlackGoldAncientSword.Modules.UI.TeamInfo.ViewModels;

namespace BlackGoldAncientSword.Modules.UI.TeamInfo.Views
{
    public partial class TeamInfoPage : UserControlBase
    {
        public TeamInfoPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TeamInfoPageViewModel oldVm)
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is TeamInfoPageViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
                SyncColumnWidths(newVm);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is TeamInfoPageViewModel vm)
            {
                switch (e.PropertyName)
                {
                    case nameof(TeamInfoPageViewModel.Col0Width):
                    case nameof(TeamInfoPageViewModel.Col1Width):
                    case nameof(TeamInfoPageViewModel.Col2Width):
                    case nameof(TeamInfoPageViewModel.Col3Width):
                    case nameof(TeamInfoPageViewModel.Col4Width):
                        SyncColumnWidths(vm);
                        break;
                }
            }
        }

        private void SyncColumnWidths(TeamInfoPageViewModel vm)
        {
            if (MainContentGrid.ColumnDefinitions.Count < 5) return;
            MainContentGrid.ColumnDefinitions[0].Width = vm.Col0Width;
            MainContentGrid.ColumnDefinitions[1].Width = vm.Col1Width;
            MainContentGrid.ColumnDefinitions[2].Width = vm.Col2Width;
            MainContentGrid.ColumnDefinitions[3].Width = vm.Col3Width;
            MainContentGrid.ColumnDefinitions[4].Width = vm.Col4Width;
        }
    }
}
