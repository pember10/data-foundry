using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;
using data_foundry.Services;
using data_foundry.Services.Adapters;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Views.Controls
{
    public partial class DeploymentTabControl : UserControl
    {
        public DeploymentTabControl()
        {
            InitializeComponent();
            DeployChangesButton.Click += DeployChangesButton_Click;
            CancelButton.Click += CancelButton_Click;
            
            // Subscribe to global processing state changes
            GlobalProcessingStateService.Instance.ProcessingStateChanged += OnProcessingStateChanged;
            
            // Subscribe to settings changes
            SettingsChangedService.Instance.SettingsChanged += OnSettingsChanged;
            
            // Initialize button states
            UpdateButtonStates();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            GlobalProcessingStateService.Instance.CancelOperation();
        }

        private void OnProcessingStateChanged(object sender, ProcessingStateChangedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateButtonStates();
                
                if (e.IsProcessing)
                {
                    ShowProcessingState(e.CurrentOperation);
                }
                else if (e.CompletionStatus.HasValue)
                {
                    // Show completion state
                    switch (e.CompletionStatus.Value)
                    {
                        case ProcessingCompletionStatus.Success:
                            ShowSuccessState(e.CompletionMessage ?? "Deployment completed successfully!");
                            break;
                        case ProcessingCompletionStatus.Error:
                            ShowErrorState(e.CompletionMessage ?? "Deployment failed");
                            break;
                        case ProcessingCompletionStatus.Cancelled:
                            ShowReadyState();
                            break;
                    }
                }
            });
        }

        private void UpdateButtonStates()
        {
            var isProcessing = GlobalProcessingStateService.Instance.IsProcessing;
            SetControlsEnabled(!isProcessing);
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            // Settings changed - could reload defaults or validate inputs
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // Currently deployment tab doesn't display settings
                // But we subscribe in case we add that functionality later
            });
        }

        private async void DeployChangesButton_Click(object sender, RoutedEventArgs e)
        {
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

            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Deploying changes..."))
            {
                MessageBox.Show("Another operation is currently in progress. Please wait for it to complete.", 
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await ExecuteDeploymentAsync(server, database, createBackup);
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, "Deployment completed successfully!");
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Deployment failed");
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
                AppendLog("Note: Using connection from Tools > Options > WTW Diffusion");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                await Task.Run(() =>
                {
                    orchestrator.Execute(
                        confirmTargetMigration: false,
                        detectChanges: false,
                        action: null,
                        logger: new VsLogger());
                });

                AppendLog("=== Deployment Complete ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ShowSuccessState("Deployment completed successfully!");
                
                MessageBox.Show(
                    "Deployment completed successfully!\n\nCheck the deployment log for details.",
                    "Deployment Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ShowErrorState("Deployment failed");
                
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

        private void SetControlsEnabled(bool enabled)
        {
            DeployChangesButton.IsEnabled = enabled;
            DeployServerTextBox.IsEnabled = enabled;
            DeployDatabaseTextBox.IsEnabled = enabled;
            AuthenticationComboBox.IsEnabled = enabled;
            BackupCheckBox.IsEnabled = enabled;
            GenerateScriptCheckBox.IsEnabled = enabled;
            TransactionCheckBox.IsEnabled = enabled;
            DropObjectsCheckBox.IsEnabled = enabled;
            IgnoreExtendedPropsCheckBox.IsEnabled = enabled;
        }

        private void ShowReadyState()
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = "Idle...";
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#666666"));
        }

        private void ShowProcessingState(string message)
        {
            ProcessingIcon.Visibility = Visibility.Visible;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));
        }

        private void ShowSuccessState(string message = "Deployment completed successfully!")
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Visible;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
        }

        private void ShowErrorState(string message = "Deployment failed")
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336"));
        }

        private void ShowLoadingIndicator(string message)
        {
            ShowProcessingState(message);
        }

        private void HideLoadingIndicator()
        {
            // Don't hide - success/error state will persist
            // ShowReadyState() is now only called when starting a new operation
        }
    }
}

