using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using data_foundry.Models;
using data_foundry.Services;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Views.Controls
{
    public partial class ChangesTabControl : UserControl
    {
        private ObservableCollection<TableChangeSummary> _allChanges;

        public ChangesTabControl()
        {
            InitializeComponent();
            InitializeDummyData();
            RefreshChangesBtn.Click += RefreshChangesButton_Click;
            GenerateScriptBtn.Click += GenerateScriptButton_Click;
            RevertChangesBtn.Click += RevertChangesButton_Click;
            ChangeTypeFilter.SelectionChanged += ChangeTypeFilter_SelectionChanged;
            CancelButton.Click += CancelButton_Click;
            ApplyPendingMigrationsBtn.Click += ApplyPendingMigrationsButton_Click;
            
            // Subscribe to global processing state changes
            GlobalProcessingStateService.Instance.ProcessingStateChanged += OnProcessingStateChanged;
            
            // Subscribe to shared results updates
            ChangeDetectionResultsService.Instance.ResultsUpdated += OnResultsUpdated;
            
            // Subscribe to database sync status changes
            DatabaseSyncStatusService.Instance.SyncStatusChanged += OnDatabaseSyncStatusChanged;
            
            // Subscribe to settings changes
            SettingsChangedService.Instance.SettingsChanged += OnSettingsChanged;
            
            // Initialize button states
            UpdateButtonStates();
            
            // Load any existing results
            var existingResults = ChangeDetectionResultsService.Instance.LatestResults;
            if (existingResults != null)
            {
                UpdateGridWithResults(existingResults);
            }
            
            // Check sync status on load
            _ = CheckSyncStatusAsync();
        }

        private async void OnResultsUpdated(object sender, List<TableChangeSummary> results)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (results != null)
            {
                UpdateGridWithResults(results);
            }
        }

        private void UpdateGridWithResults(List<TableChangeSummary> results)
        {
            _allChanges.Clear();
            foreach (var change in results)
            {
                _allChanges.Add(change);
            }
            
            ApplyCurrentFilter();
            UpdateChangesCount();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            GlobalProcessingStateService.Instance.CancelOperation();
        }

        private async void OnProcessingStateChanged(object sender, ProcessingStateChangedEventArgs e)
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
        }

        private async void OnDatabaseSyncStatusChanged(object sender, EventArgs e)
        {
            // Re-check sync status when notified of changes
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await CheckSyncStatusAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating sync status: {ex.Message}");
            }
        }

        private async void OnSettingsChanged(object sender, EventArgs e)
        {
            // Refresh sync status and re-check changes when settings change
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await CheckSyncStatusAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing after settings change: {ex.Message}");
            }
        }

        private void UpdateButtonStates()
        {
            var isProcessing = GlobalProcessingStateService.Instance.IsProcessing;
            SetButtonsEnabled(!isProcessing);
        }

        private void InitializeDummyData()
        {
            // Start with empty data - will be populated on refresh
            _allChanges = new ObservableCollection<TableChangeSummary>();
            ChangesDataGrid.ItemsSource = _allChanges;
            UpdateChangesCount();
        }

        private async void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            // First check if local DB is in sync
            var syncStatus = await CheckSyncStatusAsync();
            if (!syncStatus.IsInSync)
            {
                // No message box - sync status panel is already visible with warning
                return;
            }
            
            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Detecting changes..."))
            {
                // No message box - status indicator will show processing state
                return;
            }

            try
            {
                await DetectChangesAsync();
                
                // Success - determine message based on results
                var changesWithDiffs = _allChanges?.Count(c => c.HasChanges) ?? 0;
                var message = changesWithDiffs > 0 
                    ? $"Found {changesWithDiffs} table(s) with changes"
                    : "No changes detected";
                    
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, message);
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Change detection failed");
            }
        }

        private async void ApplyPendingMigrationsButton_Click(object sender, RoutedEventArgs e)
        {
            var syncStatus = await CheckSyncStatusAsync();
            if (syncStatus.IsInSync)
            {
                // Already in sync - just return, status will show it
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
                // No message box - status indicator will show processing state
                return;
            }

            try
            {
                await ApplyPendingMigrationsAsync(syncStatus.PendingMigrations);
                
                // Re-check sync status
                await CheckSyncStatusAsync();
            }
            catch (Exception ex)
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, 
                    "Failed to apply migrations");
                
                // Log error but don't show message box - error icon and status text are visible
                OutputWindowLogger.LogError($"Failed to apply migrations: {ex.Message}");
            }
        }

        private async Task<SyncStatus> CheckSyncStatusAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Show loading state while checking
                FilterToolbarPanel.Visibility = Visibility.Collapsed;
                SyncStatusPanel.Visibility = Visibility.Collapsed;
                ShowProcessingState("Checking for pending migrations...");
                
                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();
                List<MigrationInfo> pendingMigrations = null;
                
                await Task.Run(() =>
                {
                    pendingMigrations = orchestrator.GetPendingMigrationsForTarget();
                });
                
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                var isInSync = pendingMigrations == null || pendingMigrations.Count == 0;
                
                if (isInSync)
                {
                    SyncStatusPanel.Visibility = Visibility.Collapsed;
                    FilterToolbarPanel.Visibility = Visibility.Visible;
                    ShowReadyState();
                }
                else
                {
                    SyncStatusPanel.Visibility = Visibility.Visible;
                    FilterToolbarPanel.Visibility = Visibility.Collapsed;
                    SyncStatusText.Text = $"{pendingMigrations.Count} pending migration{(pendingMigrations.Count > 1 ? "s" : "")}";
                    ShowReadyState();
                }
                
                return new SyncStatus
                {
                    IsInSync = isInSync,
                    PendingCount = pendingMigrations?.Count ?? 0,
                    PendingMigrations = pendingMigrations ?? new List<MigrationInfo>()
                };
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Failed to check sync status: {ex.Message}");
                
                // Show ready state on error and assume in sync (show toolbar)
                ShowReadyState();
                FilterToolbarPanel.Visibility = Visibility.Visible;
                SyncStatusPanel.Visibility = Visibility.Collapsed;
                
                return new SyncStatus { IsInSync = true, PendingCount = 0, PendingMigrations = new List<MigrationInfo>() };
            }
        }

        private async Task ApplyPendingMigrationsAsync(List<MigrationInfo> pendingMigrations)
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
                
                // No success message box - status indicator shows success
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
            public List<MigrationInfo> PendingMigrations { get; set; }
        }

        private async void GenerateScriptButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are any changes
            var changesWithDiffs = _allChanges?.Where(c => c.HasChanges).ToList();
            if (changesWithDiffs == null || changesWithDiffs.Count == 0)
            {
                // No message box - status already shows "No changes detected"
                return;
            }

            // Confirm generation - keep this message box as requested
            var confirmResult = MessageBox.Show(
                $"Generate migration script for {changesWithDiffs.Count} table(s) with changes?\n\n" +
                string.Join("\n", changesWithDiffs.Select(c => $"• {c.Table}")),
                "Confirm Script Generation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Generating migration script..."))
            {
                // No message box - status indicator will show processing state
                return;
            }

            try
            {
                await GenerateMigrationScriptAsync(changesWithDiffs);
            }
            finally
            {
                GlobalProcessingStateService.Instance.CompleteProcessing();
            }
        }

        private async void RevertChangesButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are any changes
            var changesWithDiffs = _allChanges?.Where(c => c.HasChanges).ToList();
            if (changesWithDiffs == null || changesWithDiffs.Count == 0)
            {
                MessageBox.Show(
                    "No changes detected to revert.",
                    "No Changes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Confirm revert with strong warning
            var confirmResult = MessageBox.Show(
                $"?? REVERT CHANGES - WARNING ??\n\n" +
                $"This will PERMANENTLY DELETE data changes in your local database for {changesWithDiffs.Count} table(s):\n\n" +
                string.Join("\n", changesWithDiffs.Select(c => $"• {c.Table}")) + "\n\n" +
                $"Your database will be synchronized to match the clean migration state.\n\n" +
                $"This action CANNOT be undone!\n\n" +
                $"Do you want to continue?",
                "Confirm Revert Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            if (!GlobalProcessingStateService.Instance.TryStartProcessing("Reverting changes..."))
            {
                return;
            }

            try
            {
                await RevertChangesAsync(changesWithDiffs);
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Success, 
                    $"Reverted changes for {changesWithDiffs.Count} table(s)");
                
                // Refresh changes to show new state
                await DetectChangesAsync();
            }
            catch
            {
                GlobalProcessingStateService.Instance.CompleteProcessing(ProcessingCompletionStatus.Error, "Revert failed");
            }
        }

        private async Task DetectChangesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Detecting Database Changes ===");

                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                var changes = (List<TableChangeSummary>)null;

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

                // Update UI on main thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                _allChanges.Clear();
                if (changes != null)
                {
                    foreach (var change in changes)
                    {
                        _allChanges.Add(change);
                    }
                    
                    // Update shared service
                    ChangeDetectionResultsService.Instance.UpdateResults(changes);
                }

                ApplyCurrentFilter();
                UpdateChangesCount();

                var changesWithDiffs = changes?.Count(c => c.HasChanges) ?? 0;
                if (changesWithDiffs > 0)
                {
                    ShowSuccessState($"Found {changesWithDiffs} table(s) with changes");
                    // No message box - grid shows details, status shows count
                }
                else
                {
                    ShowSuccessState("No changes detected");
                    // No message box - status indicator shows result
                }
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                ShowErrorState("Change detection failed");
                
                OutputWindowLogger.LogError($"Change detection failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                // No message box - error icon and status text are visible
                // Output window has full details
                
                throw; // Re-throw to be caught by outer try-catch
            }
        }

        private async Task GenerateMigrationScriptAsync(System.Collections.Generic.List<TableChangeSummary> changesWithDiffs)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Generating Migration Script ===");

                // Create orchestrator on UI thread
                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();
                
                // Generate script name with custom format: 001_US000000_1_202501011123.sql
                var scriptName = GenerateScriptFileName();
                
                OutputWindowLogger.Log($"Script name: {scriptName}");
                
                // Get table names
                var tableNames = changesWithDiffs.Select(c => c.Table).ToList();
                
                string scriptFilePath = null;

                // Now run generation on background thread
                await Task.Run(() =>
                {
                    scriptFilePath = orchestrator.GenerateMigrationScriptWithName(tableNames, scriptName);
                });

                OutputWindowLogger.Log($"Migration script generated: {scriptFilePath}");
                OutputWindowLogger.Log("=== Script Generation Complete ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Show success in status indicator
                ShowSuccessState($"Generated {System.IO.Path.GetFileName(scriptFilePath)}");
                
                // No message box - success status and output window show details
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Script generation failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                ShowErrorState("Script generation failed");
                
                // No message box - error icon and output window show details
            }
        }

        private async Task RevertChangesAsync(System.Collections.Generic.List<TableChangeSummary> changesWithDiffs)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                OutputWindowLogger.Clear();
                OutputWindowLogger.Show();
                OutputWindowLogger.Log("=== Reverting Database Changes ===");

                // Create orchestrator on UI thread
                var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();
                
                // Get table names
                var tableNames = changesWithDiffs.Select(c => c.Table).ToList();
                
                OutputWindowLogger.Log($"Reverting changes for {tableNames.Count} table(s):");
                foreach (var table in tableNames)
                {
                    OutputWindowLogger.Log($"  • {table}");
                }

                // Run revert on background thread
                await Task.Run(() =>
                {
                    orchestrator.RevertChanges(tableNames, msg =>
                    {
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            OutputWindowLogger.Log(msg);
                        });
                    });
                });

                OutputWindowLogger.Log("=== Revert Complete ===");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // Show success in status indicator
                ShowSuccessState($"Reverted {tableNames.Count} table(s)");
                
                MessageBox.Show(
                    $"Successfully reverted changes for {tableNames.Count} table(s)!\n\n" +
                    "Your database now matches the clean migration state.",
                    "Revert Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OutputWindowLogger.LogError($"Revert failed: {ex.Message}");
                OutputWindowLogger.LogError(ex.StackTrace);

                ShowErrorState("Revert failed");
                
                MessageBox.Show(
                    $"Failed to revert changes:\n\n{ex.Message}\n\nCheck the Output window for details.",
                    "Revert Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string GenerateScriptFileName()
        {            
            // Format: 001_US000000_1_202501011123.sql (always 001)
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
            return $"001_US000000_1_{timestamp}";
        }

        private void ChangeTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrentFilter();
        }

        private void ApplyCurrentFilter()
        {
            if (ChangeTypeFilter?.SelectedItem == null || ChangesDataGrid == null || _allChanges == null)
                return;

            var selectedFilter = ((ComboBoxItem)ChangeTypeFilter.SelectedItem).Content.ToString();
            
            if (selectedFilter == "All Changes")
            {
                ChangesDataGrid.ItemsSource = _allChanges;
            }
            else if (selectedFilter == "With Changes Only")
            {
                var filtered = new ObservableCollection<TableChangeSummary>(
                    _allChanges.Where(c => c.HasChanges));
                ChangesDataGrid.ItemsSource = filtered;
            }
            else if (selectedFilter == "No Changes")
            {
                var filtered = new ObservableCollection<TableChangeSummary>(
                    _allChanges.Where(c => !c.HasChanges));
                ChangesDataGrid.ItemsSource = filtered;
            }

            UpdateChangesCount();
        }

        private void UpdateChangesCount()
        {
            if (ChangesDataGrid?.Items != null && ChangesCountText != null)
            {
                var totalChanges = _allChanges?.Count(c => c.HasChanges) ?? 0;
                var displayedCount = ChangesDataGrid.Items.Count;
                ChangesCountText.Text = $"Showing {displayedCount} table(s) ({totalChanges} with changes)";
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            RefreshChangesBtn.IsEnabled = enabled;
            RevertChangesBtn.IsEnabled = enabled;
            GenerateScriptBtn.IsEnabled = enabled;
            ChangeTypeFilter.IsEnabled = enabled;
        }

        private void ShowReadyState()
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = "Idle...";
            LoadingStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#666666"));
        }

        private void ShowProcessingState(string message)
        {
            ProcessingIcon.Visibility = Visibility.Visible;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#2196F3"));
        }

        private void ShowSuccessState(string message = "Completed successfully!")
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Visible;
            ErrorIcon.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#4CAF50"));
        }

        private void ShowErrorState(string message = "Operation failed")
        {
            ProcessingIcon.Visibility = Visibility.Collapsed;
            SuccessIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            
            LoadingStatusText.Text = message;
            LoadingStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#F44336"));
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
