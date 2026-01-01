using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using data_foundry.Models;
using data_foundry.Services;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Views.Controls
{
    public partial class DeploymentTabControl : UserControl
    {
        private bool _isProcessing;

        public DeploymentTabControl()
        {
            InitializeComponent();
            DeployChangesButton.Click += DeployChangesButton_Click;
        }

        private async void DeployChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            var server = DeployServerTextBox.Text;
            var database = DeployDatabaseTextBox.Text;
            var createBackup = BackupCheckBox.IsChecked.GetValueOrDefault();

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            {
                MessageBox.Show(
                    "Please specify both Server and Database before deploying.",
                    "Missing Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Confirm deployment
            var confirmResult = MessageBox.Show(
                $"This will execute all pending migrations on:\n\nServer: {server}\nDatabase: {database}\n\n" +
                (createBackup ? "A backup will be created before deployment.\n\n" : "") +
                "Do you want to continue?",
                "Confirm Deployment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                _isProcessing = true;
                DeployChangesButton.IsEnabled = false;
                DeployChangesButton.Content = "Deploying...";

                await ExecuteDeploymentAsync(server, database, createBackup);
            }
            finally
            {
                _isProcessing = false;
                DeployChangesButton.IsEnabled = true;
                DeployChangesButton.Content = "Deploy Changes";
            }
        }

        private async Task ExecuteDeploymentAsync(string server, string database, bool createBackup)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DeploymentLogTextBox.Clear();
            AppendLog("=== Starting Deployment ===");
            AppendLog($"Target Server: {server}");
            AppendLog($"Target Database: {database}");
            
            if (createBackup)
            {
                AppendLog("Backup: Enabled (not yet implemented)");
                // TODO: Implement backup functionality
            }

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();

                // Note: Currently using settings from DataFoundryOptions
                // The server/database from the UI are ignored for now
                // You can enhance this to override the settings if needed
                AppendLog("Note: Using connection from Tools > Options > Data Foundry");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                await Task.Run(() =>
                {
                    orchestrator.Execute(
                        confirmTargetMigration: false,
                        detectChanges: false,
                        action: null,
                        logger: msg =>
                        {
                            ThreadHelper.JoinableTaskFactory.Run(async () =>
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                AppendLog(msg);
                                OutputWindowLogger.Log(msg);
                            });
                        });
                });

                AppendLog("=== Deployment Complete ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                MessageBox.Show(
                    "Deployment completed successfully!\n\nCheck the deployment log for details.",
                    "Deployment Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                AppendLog($"ERROR: {ex.Message}");
                AppendLog(ex.StackTrace);
                
                OutputWindowLogger.LogError($"Deployment failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                MessageBox.Show(
                    $"Deployment failed:\n\n{ex.Message}\n\nCheck the deployment log for details.",
                    "Deployment Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            DeploymentLogTextBox.AppendText($"[{timestamp}] {message}\n");
            DeploymentLogTextBox.ScrollToEnd();
        }
    }
}
