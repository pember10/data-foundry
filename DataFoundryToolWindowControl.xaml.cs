using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using data_foundry.Models;

namespace data_foundry
{
    public partial class DataFoundryToolWindowControl : UserControl
    {
        private ObservableCollection<DatabaseChange> _allChanges;

        public DataFoundryToolWindowControl()
        {
            InitializeComponent();
            InitializeDummyData();
            WireUpEventHandlers();
        }

        private void InitializeDummyData()
        {
            // Initialize dummy database changes
            _allChanges = new ObservableCollection<DatabaseChange>
            {
                new DatabaseChange
                {
                    Type = "Table",
                    ObjectName = "Users",
                    Schema = "dbo",
                    ChangeType = "Modified",
                    ModifiedDate = "2025-01-11 14:15:22"
                },
                new DatabaseChange
                {
                    Type = "Stored Procedure",
                    ObjectName = "GetUserById",
                    Schema = "dbo",
                    ChangeType = "Added",
                    ModifiedDate = "2025-01-11 13:45:10"
                },
                new DatabaseChange
                {
                    Type = "Table",
                    ObjectName = "Orders",
                    Schema = "dbo",
                    ChangeType = "Modified",
                    ModifiedDate = "2025-01-11 12:30:05"
                },
                new DatabaseChange
                {
                    Type = "View",
                    ObjectName = "vw_ActiveUsers",
                    Schema = "dbo",
                    ChangeType = "Modified",
                    ModifiedDate = "2025-01-11 11:22:33"
                },
                new DatabaseChange
                {
                    Type = "Function",
                    ObjectName = "fn_CalculateTotal",
                    Schema = "dbo",
                    ChangeType = "Added",
                    ModifiedDate = "2025-01-11 10:15:44"
                },
                new DatabaseChange
                {
                    Type = "Stored Procedure",
                    ObjectName = "UpdateOrderStatus",
                    Schema = "dbo",
                    ChangeType = "Modified",
                    ModifiedDate = "2025-01-11 09:50:12"
                },
                new DatabaseChange
                {
                    Type = "Table",
                    ObjectName = "Products",
                    Schema = "dbo",
                    ChangeType = "Modified",
                    ModifiedDate = "2025-01-11 09:30:00"
                },
                new DatabaseChange
                {
                    Type = "Trigger",
                    ObjectName = "trg_AuditUsers",
                    Schema = "dbo",
                    ChangeType = "Added",
                    ModifiedDate = "2025-01-11 08:45:55"
                }
            };

            ChangesDataGrid.ItemsSource = _allChanges;
            UpdateChangesCount();
        }

        private void WireUpEventHandlers()
        {
            // Overview tab buttons
            RefreshChangesButton.Click += RefreshChangesButton_Click;
            CompareDatabasesButton.Click += CompareDatabasesButton_Click;
            DeployButton.Click += DeployButton_Click;

            // Changes tab
            RefreshChangesBtn.Click += RefreshChangesButton_Click;
            ChangeTypeFilter.SelectionChanged += ChangeTypeFilter_SelectionChanged;

            // Deployment tab
            DeployChangesButton.Click += DeployChangesButton_Click;

            // Settings tab
            BrowseScriptsButton.Click += BrowseScriptsButton_Click;
            SaveSettingsButton.Click += SaveSettingsButton_Click;
        }

        private void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            // This will eventually call PowerShell scripts
            DeploymentLogTextBox.AppendText($"\n[{DateTime.Now:HH:mm:ss}] Refreshing database changes...");
            MessageBox.Show("Refreshing database changes.\n\nThis will eventually execute PowerShell scripts to detect changes.", 
                "Refresh Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CompareDatabasesButton_Click(object sender, RoutedEventArgs e)
        {
            DeploymentLogTextBox.AppendText($"\n[{DateTime.Now:HH:mm:ss}] Starting database comparison...");
            MessageBox.Show("Comparing databases.\n\nThis will eventually execute PowerShell scripts to compare schema differences.", 
                "Compare Databases", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            DeployChangesButton_Click(sender, e);
        }

        private void DeployChangesButton_Click(object sender, RoutedEventArgs e)
        {
            var server = DeployServerTextBox.Text;
            var database = DeployDatabaseTextBox.Text;
            var createBackup = BackupCheckBox.IsChecked == true;

            DeploymentLogTextBox.Text = $"[{DateTime.Now:HH:mm:ss}] Starting deployment...\n";
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Target Server: {server}\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Target Database: {database}\n");
            
            if (createBackup)
            {
                DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Creating backup...\n");
            }

            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Analyzing changes...\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Found {_allChanges.Count} changes to deploy.\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] Ready to execute deployment.\n");
            DeploymentLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] This will eventually execute PowerShell deployment scripts.\n");

            MessageBox.Show($"Deployment prepared for:\n\nServer: {server}\nDatabase: {database}\nChanges: {_allChanges.Count}\n\nThis will eventually execute PowerShell scripts to deploy changes.", 
                "Deploy Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ChangeTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChangeTypeFilter.SelectedItem == null || ChangesDataGrid == null)
                return;

            var selectedFilter = ((ComboBoxItem)ChangeTypeFilter.SelectedItem).Content.ToString();
            
            if (selectedFilter == "All Changes")
            {
                ChangesDataGrid.ItemsSource = _allChanges;
            }
            else
            {
                var filtered = new ObservableCollection<DatabaseChange>();
                foreach (var change in _allChanges)
                {
                    if (change.Type == selectedFilter || 
                        (selectedFilter == "Stored Procedures" && change.Type == "Stored Procedure"))
                    {
                        filtered.Add(change);
                    }
                }
                ChangesDataGrid.ItemsSource = filtered;
            }
            
            UpdateChangesCount();
        }

        private void UpdateChangesCount()
        {
            if (ChangesDataGrid.Items != null && ChangesCountText != null)
            {
                ChangesCountText.Text = $"Total changes: {ChangesDataGrid.Items.Count}";
            }
        }

        private void BrowseScriptsButton_Click(object sender, RoutedEventArgs e)
        {
            // Using WPF OpenFileDialog as a workaround for folder selection
            // In a full implementation, consider using Windows API Code Pack's CommonOpenFileDialog
            var result = MessageBox.Show(
                $"Current Scripts Path:\n{ScriptsPathTextBox.Text}\n\nWould you like to change it?\n\n(Full folder browser will be implemented in a future version)", 
                "Browse Scripts Folder", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Placeholder for folder browser
                // TODO: Implement proper folder browser dialog
                MessageBox.Show("Folder browser functionality will be added in the next version.\n\nFor now, please manually edit the path in the text box.", 
                    "Feature Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // This will eventually save settings to user config
            MessageBox.Show("Settings saved successfully!\n\nSettings will be persisted in future versions.", 
                "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
