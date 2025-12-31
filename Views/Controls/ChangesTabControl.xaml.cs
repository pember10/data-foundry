using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using data_foundry.Models;

namespace data_foundry.Views.Controls
{
    public partial class ChangesTabControl : UserControl
    {
        private ObservableCollection<DatabaseChange> _allChanges;

        public ChangesTabControl()
        {
            InitializeComponent();
            InitializeDummyData();
            RefreshChangesBtn.Click += RefreshChangesButton_Click;
            ChangeTypeFilter.SelectionChanged += ChangeTypeFilter_SelectionChanged;
        }

        private void InitializeDummyData()
        {
            _allChanges = new ObservableCollection<DatabaseChange>
            {
                new DatabaseChange { Type = "Table", ObjectName = "Users", Schema = "dbo", ChangeType = "Modified", ModifiedDate = "2025-01-11 14:15:22" },
                new DatabaseChange { Type = "Stored Procedure", ObjectName = "GetUserById", Schema = "dbo", ChangeType = "Added", ModifiedDate = "2025-01-11 13:45:10" },
                new DatabaseChange { Type = "Table", ObjectName = "Orders", Schema = "dbo", ChangeType = "Modified", ModifiedDate = "2025-01-11 12:30:05" },
                new DatabaseChange { Type = "View", ObjectName = "vw_ActiveUsers", Schema = "dbo", ChangeType = "Modified", ModifiedDate = "2025-01-11 11:22:33" },
                new DatabaseChange { Type = "Function", ObjectName = "fn_CalculateTotal", Schema = "dbo", ChangeType = "Added", ModifiedDate = "2025-01-11 10:15:44" },
                new DatabaseChange { Type = "Stored Procedure", ObjectName = "UpdateOrderStatus", Schema = "dbo", ChangeType = "Modified", ModifiedDate = "2025-01-11 09:50:12" },
                new DatabaseChange { Type = "Table", ObjectName = "Products", Schema = "dbo", ChangeType = "Modified", ModifiedDate = "2025-01-11 09:30:00" },
                new DatabaseChange { Type = "Trigger", ObjectName = "trg_AuditUsers", Schema = "dbo", ChangeType = "Added", ModifiedDate = "2025-01-11 08:45:55" }
            };
            ChangesDataGrid.ItemsSource = _allChanges;
            UpdateChangesCount();
        }

        private void RefreshChangesButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Refreshing database changes.\n\nThis will eventually execute PowerShell scripts to detect changes.",
                "Refresh Changes", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}
