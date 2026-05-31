namespace BlackGoldAncientSword.Modules.UI.Feedback.Views
{
    public partial class FeedbackPage
    {
        public FeedbackPage()
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
                if (DataContext is ViewModels.FeedbackPageViewModel vm)
                {
                    vm.DismissCommand.Execute();
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
