using System;
using System.Windows;
using System.Windows.Controls;

namespace data_foundry.Views.Controls
{
    public partial class DeploymentTabControl : UserControl
    {
        public DeploymentTabControl()
        {
            InitializeComponent();
            DeployChangesButton.Click += DeployChangesButton_Click;
        }

        private void DeployChangesButton_Click(object sender, RoutedEventArgs e)
        {
            var server = DeployServerTextBox.Text;
            var database = DeployDatabaseTextBox.Text;
            var createBackup = BackupCheckBox.IsChecked.GetValueOrDefault();

            DeploymentLogTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] Starting deployment...\n";
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Target Server: {server}\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Target Database: {database}\n");
            if (createBackup)
            {
                DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Creating backup...\n");
            }
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Analyzing changes...\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Ready to execute deployment.\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] This will eventually execute PowerShell deployment scripts.\n");

            MessageBox.Show($"Deployment prepared for:\n\nServer: {server}\nDatabase: {database}\n\nThis will eventually execute PowerShell scripts to deploy changes.",
                "Deploy Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
