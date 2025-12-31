using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using data_foundry.Options;

namespace data_foundry.Views.Controls
{
    public partial class OverviewTabControl : UserControl
    {
        public OverviewTabControl()
        {
            InitializeComponent();
            RefreshChangesButton.Click += RefreshChangesButton_Click;
            CompareDatabasesButton.Click += CompareDatabasesButton_Click;
            DeployButton.Click += DeployButton_Click;
            OpenSettingsButton.Click += OpenSettingsButton_Click;
            RefreshOverviewSettingsDisplay();
        }

        private void RefreshOverviewSettingsDisplay()
        {
            var package = data_foundry.data_foundryPackage.Instance;
            if (package != null)
            {
                var options = (DataFoundryOptions)package.GetDialogPage(typeof(DataFoundryOptions));
                TargetProjectNameText.Text = string.IsNullOrWhiteSpace(options.SqlProject) ? string.Empty : options.SqlProject;
                // Add similar lines for other settings if needed
            }
            else
            {
                TargetProjectNameText.Text = string.Empty;
            }
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var package = data_foundry.data_foundryPackage.Instance;
            package?.ShowOptionPage(typeof(DataFoundryOptions));
            // Refresh after dialog closes
            RefreshOverviewSettingsDisplay();
        }

        private void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Refreshing database changes.\n\nThis will eventually execute PowerShell scripts to detect changes.",
                "Refresh Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CompareDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Comparing databases.\n\nThis will eventually execute PowerShell scripts to compare schema differences.",
                "Compare Databases", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Deployment prepared.\n\nThis will eventually execute PowerShell scripts to deploy changes.",
                "Deploy Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
