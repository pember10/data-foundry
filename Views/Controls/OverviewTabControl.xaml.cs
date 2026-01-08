using System;
using System.Linq;
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
            CancelButton.Click += CancelButton_Click;
            ApplyPendingMigrationsBtn.Click += ApplyPendingMigrationsButton_Click;
            
            // Subscribe to global processing state changes
            GlobalProcessingStateService.Instance.ProcessingStateChanged += OnProcessingStateChanged;
            
            // Subscribe to database sync status changes
            DatabaseSyncStatusService.Instance.SyncStatusChanged += OnDatabaseSyncStatusChanged;
            
            // Subscribe to settings changes
            SettingsChangedService.Instance.SettingsChanged += OnSettingsChanged;
            
            // Bind activity history to the ListBox
            BindActivityHistory();
            
            RefreshOverviewSettingsDisplay();
            
            // Initialize button states
            UpdateButtonStates();
            
            // Check sync status on load
            _ = CheckSyncStatusAsync();
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
                            ShowSuccessState(e.CompletionMessage ?? "Completed successfully!");
                            break;
                        case ProcessingCompletionStatus.Error:
                            ShowErrorState(e.CompletionMessage ?? "Operation failed");
                            break;
                        case ProcessingCompletionStatus.Cancelled:
                            ShowReadyState();
                            break;
                    }
                }
            });
        }

        private async void OnDatabaseSyncStatusChanged(object sender, EventArgs e)
        {
            // Re-check sync status when notified of changes
            // Use try-catch to handle any threading or timing issues
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await CheckSyncStatusAsync();
            }
            catch (Exception ex)
            {
                // Log but don't crash
                System.Diagnostics.Debug.WriteLine($"Error updating sync status: {ex.Message}");
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            // Refresh the overview settings display when settings change
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RefreshOverviewSettingsDisplay();
            });
        }

        private void UpdateButtonStates()
        {
            var isProcessing = GlobalProcessingStateService.Instance.IsProcessing;
            SetButtonsEnabled(!isProcessing);
        }

        private void BindActivityHistory()
        {
            // Set the ItemsSource to the observable collection from ActivityHistoryService
            RecentActivityListBox.ItemsSource = ActivityHistoryService.Instance.Activities;

            // Subscribe to activity added events to update the "Last Deployment" text
            ActivityHistoryService.Instance.ActivityAdded += OnActivityAdded;

            // Subscribe to collection changed to update empty state
            ActivityHistoryService.Instance.Activities.CollectionChanged += Activities_CollectionChanged;

            // Set initial empty state
            UpdateEmptyState();
        }

        private void Activities_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateEmptyState();
            });
        }

        private void UpdateEmptyState()
        {
            var hasActivities = ActivityHistoryService.Instance.Activities.Count > 0;
            NoActivityMessage.Visibility = hasActivities ? Visibility.Collapsed : Visibility.Visible;
            RecentActivityListBox.Visibility = hasActivities ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnActivityAdded(object sender, ActivityEntry activity)
        {
            // Update Last Deployment text when a deployment activity completes successfully
            if (activity.OperationType == ActivityType.Deployment && 
                activity.Status == ActivityStatus.Success)
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    LastDeploymentText.Text = activity.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                });
            }
        }

        private void RefreshOverviewSettingsDisplay()
        {
            var package = data_foundry.data_foundryPackage.Instance;
            if (package != null)
            {
                var options = (DataFoundryOptions)package.GetDialogPage(typeof(DataFoundryOptions));
                
                // Set Target Project Name
                TargetProjectNameText.Text = string.IsNullOrWhiteSpace(options.SqlProject) 
                    ? "(Not configured)" 
                    : options.SqlProject;

                // Set Source Database
                try
                {
                    if (!string.IsNullOrWhiteSpace(options.LocalDatabaseConnection))
                    {
                        var (database, server) = ServicesHelper.ParseConnectionString(options.LocalDatabaseConnection);
                        SourceDatabaseConnectionText.Text = $"[{server}].[{database}]";
                    }
                    else
                    {
                        SourceDatabaseConnectionText.Text = "(Not configured)";
                    }
                }
                catch
                {
                    SourceDatabaseConnectionText.Text = "(Invalid connection string)";
                }

                // Initialize Last Deployment
                InitializeLastDeployment();
            }
            else
            {
                TargetProjectNameText.Text = "(Not available)";
                SourceDatabaseConnectionText.Text = "(Not available)";
                LastDeploymentText.Text = "Never";
            }
        }

        private void InitializeLastDeployment()
        {
            // Get the most recent successful deployment from activity history
            var lastDeployment = ActivityHistoryService.Instance.Activities
                .FirstOrDefault(a => a.OperationType == ActivityType.Deployment 
                    && a.Status == ActivityStatus.Success);

            if (lastDeployment != null)
            {
                LastDeploymentText.Text = lastDeployment.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                LastDeploymentText.Text = "Never";
            }
        }

        private async void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            // First check if local DB is in sync
            var syncStatus = await CheckSyncStatusAsync();
            if (!syncStatus.IsInSync)
            {
                MessageBox.Show(
                    $"Your local database is out of sync!\n\n" +
                    $"You have {syncStatus.PendingCount} pending migration(s) that must be applied before detecting changes.\n\n" +
                    $"Click 'Apply Pending Migrations' to sync your database first.",
                    "Database Out of Sync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            
            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Detecting changes..."))
            {
                MessageBox.Show("Another operation is currently in progress. Please wait for it to complete.", 
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await ExecuteChangeDetectionAsync(showDialog: false);
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, "Changes detected");
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Change detection failed");
            }
        }

        private async void CompareDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Comparing databases..."))
            {
                MessageBox.Show("Another operation is currently in progress. Please wait for it to complete.", 
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await ExecuteChangeDetectionAsync(showDialog: true);
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, "Comparison complete");
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Comparison failed");
            }
        }

        private async void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Deploying migrations..."))
            {
                MessageBox.Show("Another operation is currently in progress. Please wait for it to complete.", 
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await ExecuteMigrationsAsync();
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, "Migrations deployed successfully!");
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Migration deployment failed");
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
                
                ShowSuccessState("Migrations deployed successfully!");
                
                MessageBox.Show(
                    "Migrations executed successfully!\n\nCheck the Output window (Data Foundry pane) for details.",
                    "Migrations Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ShowErrorState("Migration deployment failed");
                
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
                                
                                // Update loading indicator with current step
                                if (LoadingStatusText != null && LoadingIndicator.Visibility == Visibility.Visible)
                                {
                                    LoadingStatusText.Text = msg;
                                }
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
                        ShowSuccessState($"Found {changesWithDiffs.Count} table(s) with changes");
                        
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
                        ShowSuccessState("No changes detected");
                        
                        MessageBox.Show("No changes detected between target and shadow databases.",
                            "No Changes", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (changes != null)
                {
                    // Silent refresh (no dialog) - still show success
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var changesWithDiffs = changes.Count(c => c.HasChanges);
                    if (changesWithDiffs > 0)
                    {
                        ShowSuccessState($"Found {changesWithDiffs} table(s) with changes");
                    }
                    else
                    {
                        ShowSuccessState("No changes detected");
                    }
                }

                // Update shared results service so Changes tab can display them
                if (changes != null)
                {
                    ChangeDetectionResultsService.Instance.UpdateResults(changes);
                }
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ShowErrorState("Change detection failed");
                
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

        private void ShowReadyState()
        {
            ReadyIcon.Visibility = Visibility.Visible;
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = "Ready for operations...";
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#666666"));
        }

        private void ShowProcessingState(string message)
        {
            ReadyIcon.Visibility = Visibility.Collapsed;
            ProcessingIcon.Visibility = Visibility.Visible;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));
        }

        private void ShowSuccessState(string message = "Completed successfully!")
        {
            ReadyIcon.Visibility = Visibility.Collapsed;
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Visible;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
        }

        private void ShowErrorState(string message = "Operation failed")
        {
            ReadyIcon.Visibility = Visibility.Collapsed;
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
        
        private async void ApplyPendingMigrationsButton_Click(object sender, RoutedEventArgs e)
        {
            var syncStatus = await CheckSyncStatusAsync();
            if (syncStatus.IsInSync)
            {
                MessageBox.Show("Your local database is already in sync!", "Already In Sync", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var confirmResult = MessageBox.Show(
                $"Apply {syncStatus.PendingCount} pending migration(s) to your local database?\n\n" +
                "This will execute all pending migration scripts in order.\n\n" +
                "Do you want to continue?",
                "Confirm Apply Migrations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Applying pending migrations..."))
            {
                MessageBox.Show("Another operation is currently in progress. Please wait for it to complete.", 
                    "Operation In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await ApplyPendingMigrationsAsync(syncStatus.PendingMigrations);
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, 
                    $"Applied {syncStatus.PendingCount} migration(s)");
                
                // Re-check sync status
                await CheckSyncStatusAsync();
            }
            catch (Exception ex)
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, 
                    "Failed to apply migrations");
                    
                MessageBox.Show(
                    $"Failed to apply migrations:\n\n{ex.Message}",
                    "Migration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task<SyncStatus> CheckSyncStatusAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Show loading state while checking
                QuickActionsPanel.Visibility = Visibility.Collapsed;
                SyncStatusPanel.Visibility = Visibility.Collapsed;
                ShowProcessingState("Checking for pending migrations...");
                
                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();
                System.Collections.Generic.List<MigrationInfo> pendingMigrations = null;
                
                await Task.Run(() =>
                {
                    pendingMigrations = orchestrator.GetPendingMigrationsForTarget();
                });
                
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                var isInSync = pendingMigrations == null || pendingMigrations.Count == 0;
                
                if (isInSync)
                {
                    SyncStatusPanel.Visibility = Visibility.Collapsed;
                    QuickActionsPanel.Visibility = Visibility.Visible;
                    ShowReadyState();
                }
                else
                {
                    SyncStatusPanel.Visibility = Visibility.Visible;
                    QuickActionsPanel.Visibility = Visibility.Collapsed;
                    SyncStatusText.Text = $"{pendingMigrations.Count} pending migration{(pendingMigrations.Count > 1 ? "s" : "")}";
                    ShowReadyState();
                }
                
                return new SyncStatus
                {
                    IsInSync = isInSync,
                    PendingCount = pendingMigrations?.Count ?? 0,
                    PendingMigrations = pendingMigrations ?? new System.Collections.Generic.List<MigrationInfo>()
                };
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Failed to check sync status: {ex.Message}");
                
                // Show ready state on error and assume in sync (show buttons)
                ShowReadyState();
                QuickActionsPanel.Visibility = Visibility.Visible;
                SyncStatusPanel.Visibility = Visibility.Collapsed;
                
                return new SyncStatus { IsInSync = true, PendingCount = 0, PendingMigrations = new System.Collections.Generic.List<MigrationInfo>() };
            }
        }

        private async Task ApplyPendingMigrationsAsync(System.Collections.Generic.List<MigrationInfo> pendingMigrations)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Applying Pending Migrations ===");
                OutputWindowLogger.Log($"Applying {pendingMigrations.Count} migration(s) to local database...");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

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

                OutputWindowLogger.Log("=== Migrations Applied Successfully ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Notify all tabs that sync status has changed
                DatabaseSyncStatusService.Instance.NotifySyncStatusChanged();
                
                MessageBox.Show(
                    $"Successfully applied {pendingMigrations.Count} migration(s) to your local database!\n\n" +
                    "Your database is now in sync with the migration scripts.",
                    "Migrations Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Failed to apply migrations: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);
                throw;
            }
        }

        private class SyncStatus
        {
            public bool IsInSync { get; set; }
            public int PendingCount { get; set; }
            public System.Collections.Generic.List<MigrationInfo> PendingMigrations { get; set; }
        }
    }
}
