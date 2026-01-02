using System.Windows;
using System.Windows.Controls;

namespace data_foundry.Views.Controls
{
    public partial class TabbedContentControl : UserControl
    {
        public TabbedContentControl()
        {
            InitializeComponent();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var package = data_foundry.data_foundryPackage.Instance;
            package?.ShowOptionPage(typeof(Options.DataFoundryOptions));
        }
    }
}
