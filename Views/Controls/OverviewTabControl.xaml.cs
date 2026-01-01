using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using data_foundry.Options;
using data_foundry.Services;
using data_foundry.Models;
using System.Threading.Tasks;

namespace data_foundry.Views.Controls
{
    public partial class OverviewTabControl : UserControl
    {
        private bool _isProcessing;

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
            RefreshOverviewSettingsDisplay();
        }

        private async void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;
                SetButtonsEnabled(false);

                await ExecuteChangeDetectionAsync();
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private async void CompareDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;
                SetButtonsEnabled(false);

                await ExecuteChangeDetectionAsync(showDialog: true);
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private async void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;
                SetButtonsEnabled(false);

                await ExecuteMigrationsAsync();
            }
            finally
            {
                _isProcessing = false;
                SetButtonsEnabled(true);
            }
        }

        private async Task ExecuteMigrationsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Starting Migration Execution ===");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                // Execute migrations on target database
                await Task.Run(() =>
                {
                    orchestrator.ExecuteTargetMigrations(
                        requireConfirmation: false,
                        logger: msg =>
                        {
                            ThreadHelper.JoinableTaskFactory.Run(async () =>
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                OutputWindowLogger.Log(msg);
                            });
                        });
                });

                OutputWindowLogger.Log("=== Migration Execution Complete ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                MessageBox.Show(
                    "Migrations executed successfully!\n\nCheck the Output window (Data Foundry pane) for details.",
                    "Migrations Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Migration failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                MessageBox.Show(
                    $"Migration execution failed:\n\n{ex.Message}\n\nCheck the Output window for details.",
                    "Migration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ExecuteChangeDetectionAsync(bool showDialog = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Starting Change Detection ===");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                System.Collections.Generic.List<TableChangeSummary> changes = null;

                await Task.Run(() =>
                {
                    changes = orchestrator.DetectAndHandleChanges(
                        action: null, // Don't auto-act on changes
                        logger: msg =>
                        {
                            ThreadHelper.JoinableTaskFactory.Run(async () =>
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                OutputWindowLogger.Log(msg);
                            });
                        });
                });

                OutputWindowLogger.Log("=== Change Detection Complete ===");

                if (showDialog && changes != null)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var changesWithDiffs = changes.FindAll(c => c.HasChanges);

                    if (changesWithDiffs.Count > 0)
                    {
                        var message = "Changes detected:\n\n";
                        foreach (var change in changesWithDiffs)
                        {
                            message += $"• {change.Table}: {change.Inserts} inserts, {change.Updates} updates, {change.Deletes} deletes\n";
                        }
                        message += "\nCheck the Output window for full details.";

                        MessageBox.Show(message, "Database Changes Detected",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No changes detected between target and shadow databases.",
                            "No Changes", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Change detection failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                MessageBox.Show(
                    $"Change detection failed:\n\n{ex.Message}\n\nCheck the Output window for details.",
                    "Detection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            RefreshChangesButton.IsEnabled = enabled;
            CompareDatabasesButton.IsEnabled = enabled;
            DeployButton.IsEnabled = enabled;
        }
    }
}
