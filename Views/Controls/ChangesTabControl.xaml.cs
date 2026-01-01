using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using data_foundry.Models;
using data_foundry.Services;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Views.Controls
{
    public partial class ChangesTabControl : UserControl
    {
        private ObservableCollection<TableChangeSummary> _allChanges;
        private bool _isProcessing;

        public ChangesTabControl()
        {
            InitializeComponent();
            InitializeDummyData();
            RefreshChangesBtn.Click += RefreshChangesButton_Click;
            ChangeTypeFilter.SelectionChanged += ChangeTypeFilter_SelectionChanged;
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
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;
                RefreshChangesBtn.IsEnabled = false;
                RefreshChangesBtn.Content = "Detecting Changes...";

                await DetectChangesAsync();
            }
            finally
            {
                _isProcessing = false;
                RefreshChangesBtn.IsEnabled = true;
                RefreshChangesBtn.Content = "Refresh Changes";
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

                // Update UI on main thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                _allChanges.Clear();
                if (changes != null)
                {
                    foreach (var change in changes)
                    {
                        _allChanges.Add(change);
                    }
                }

                ApplyCurrentFilter();
                UpdateChangesCount();

                var changesWithDiffs = changes?.Count(c => c.HasChanges) ?? 0;
                if (changesWithDiffs > 0)
                {
                    MessageBox.Show(
                        $"Found {changesWithDiffs} table(s) with changes.\n\nSee the grid below for details.",
                        "Changes Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "No changes detected between target and shadow databases.",
                        "No Changes",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
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
    }
}
