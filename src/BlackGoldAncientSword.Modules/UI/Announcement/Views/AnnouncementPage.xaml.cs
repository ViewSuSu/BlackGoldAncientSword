namespace BlackGoldAncientSword.Modules.UI.Announcement.Views
{
    public partial class AnnouncementPage
    {
        public AnnouncementPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (FindName("OverlayGrid") is System.Windows.Controls.Grid grid)
                {
                    grid.Focus();
                }
            };
        }

        private void Grid_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (DataContext is ViewModels.AnnouncementPageViewModel vm)
                {
                    vm.DismissCommand.Execute();
                }
            }
        }
    }
}