using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

#pragma warning disable VSTHRD100 // Avoid async void methods — all async void here are WPF event handlers with try/catch
namespace data_foundry.Views.Controls
{
    public partial class SettingsTabControl : UserControl
    {
        private static string[] ExcludedFolders => new[] { "Properties", "bin", "obj", "References" };

        public SettingsTabControl()
        {
            InitializeComponent();
            Loaded += SettingsTabControl_Loaded;
            SqlProjectComboBox.SelectionChanged += SqlProjectComboBox_SelectionChanged;
            MigrationsFolderComboBox.IsEnabled = false;
            SaveSettingsButton.Click += SaveSettingsButton_Click;
        }

        private async void SettingsTabControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                PopulateSqlProjectComboBox();
            }
            catch (Exception ex)
            {
                // Log the exception - async void methods can crash the process if unhandled
                System.Diagnostics.Debug.WriteLine($"Error loading settings tab: {ex}");
                MessageBox.Show($"Failed to load SQL projects: {ex.Message}", 
                    "Load Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
        }

        private void PopulateSqlProjectComboBox()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            SqlProjectComboBox.Items.Clear();
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte?.Solution == null || dte.Solution.Projects == null)
                return;
            ComboBoxItem firstItem = null;
            foreach (Project project in dte.Solution.Projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName))
                    continue;
                if (project.FullName.ToLowerInvariant().EndsWith(".sqlproj"))
                {
                    var item = new ComboBoxItem { Content = project.Name, Tag = project };
                    SqlProjectComboBox.Items.Add(item);
                    if (firstItem == null)
                        firstItem = item;
                }
            }
            if (SqlProjectComboBox.Items.Count > 0)
            {
                firstItem.IsSelected = true;
                // If only one project, immediately populate folders
                if (SqlProjectComboBox.Items.Count == 1)
                {
                    var project = firstItem.Tag as Project;
                    if (project != null)
                        PopulateMigrationsFolderComboBoxFromDte(project);
                }
            }
        }

        private void SqlProjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            MigrationsFolderComboBox.Items.Clear();
            MigrationsFolderComboBox.IsEnabled = false;
            if (!(SqlProjectComboBox.SelectedItem is ComboBoxItem selectedItem))
                return;
            if (!(selectedItem.Tag is Project project))
                return;
            PopulateMigrationsFolderComboBoxFromDte(project);
        }

        private void PopulateMigrationsFolderComboBoxFromDte(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            MigrationsFolderComboBox.Items.Clear();
            foreach (ProjectItem item in project.ProjectItems)
            {
                if (string.Equals(item.Kind, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase) &&
                    !ExcludedFolders.Any(f =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        return string.Equals(f, item.Name, StringComparison.OrdinalIgnoreCase);
                    }))
                {
                    var folderName = item.Name;
                    var comboItem = new ComboBoxItem { Content = folderName, Tag = item };
                    MigrationsFolderComboBox.Items.Add(comboItem);
                }
            }
            if (MigrationsFolderComboBox.Items.Count > 0)
            {
                ((ComboBoxItem)MigrationsFolderComboBox.Items[0]).IsSelected = true;
                MigrationsFolderComboBox.IsEnabled = true;
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings saved successfully!\n\nSettings will be persisted in future versions.",
                "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
