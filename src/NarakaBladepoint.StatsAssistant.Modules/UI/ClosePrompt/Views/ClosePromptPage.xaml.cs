namespace NarakaBladepoint.StatsAssistant.Modules.UI.ClosePrompt.Views
{
    public partial class ClosePromptPage
    {
        public ClosePromptPage()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                // Focus the overlay grid so it can receive keyboard events
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
                DismissOverlay();
            }
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DismissOverlay();
        }

        private void DismissOverlay()
        {
            // Remove this view from the ClosePromptRegion
            var rm = NarakaBladepoint.StatsAssistant.Framework.Core.Bases.PrismApplicationBase.ContainerProvider
                .Resolve<Prism.Regions.IRegionManager>();
            var region = rm.Regions[NarakaBladepoint.StatsAssistant.Framework.Core.Consts.GlobalConstant.ClosePromptRegion];
            region.RemoveAll();
        }
    }
}