using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using data_foundry.Views.Controls;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace data_foundry
{
    public partial class DataFoundryToolWindowControl : UserControl
    {
        private DTE _dte;
        private SolutionEvents _solutionEvents;

        public DataFoundryToolWindowControl()
        {
            InitializeComponent();
            
            // Defer initialization to avoid blocking the constructor
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Only initialize once
            Loaded -= OnLoaded;
            
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                InitializeDteEvents();
                UpdateContent();
            }
            catch (Exception ex)
            {
                // Log the exception - async void methods can crash the process if unhandled
                System.Diagnostics.Debug.WriteLine($"Error initializing DataFoundry tool window: {ex}");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                MessageBox.Show($"Failed to initialize Data Foundry: {ex.Message}", 
                    "Initialization Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }

        private void InitializeDteEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (_dte != null && _dte.Events != null)
            {
                _solutionEvents = _dte.Events.SolutionEvents;
                _solutionEvents.Opened += SolutionOrProjectChanged_NoArgs;
                _solutionEvents.AfterClosing += SolutionOrProjectChanged_NoArgs;
                _solutionEvents.ProjectAdded += SolutionOrProjectChanged_Project;
                _solutionEvents.ProjectRemoved += SolutionOrProjectChanged_Project;
            }
        }

        private void SolutionOrProjectChanged_NoArgs()
        {
            // Fire and forget is acceptable for event handlers that update UI
            _ = UpdateContentAsync();
        }

        private void SolutionOrProjectChanged_Project(Project proj)
        {
            // Fire and forget is acceptable for event handlers that update UI
            _ = UpdateContentAsync();
        }

        private async Task UpdateContentAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            UpdateContent();
        }

        private void UpdateContent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            if (HasSqlProjectInSolution())
            {
                MainContent.Content = new TabbedContentControl();
            }
            else
            {
                MainContent.Content = new NoSqlProjectMessageControl();
            }
        }

        private bool HasSqlProjectInSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte?.Solution == null || _dte.Solution.Projects == null)
                return false;

            foreach (Project project in _dte.Solution.Projects)
            {
                try
                {
                    if (project == null || string.IsNullOrWhiteSpace(project.FullName))
                    {
                        // Prefer early return
                        continue;
                    }

                    if (project.FullName.ToLowerInvariant().EndsWith(".sqlproj"))
                    {
                        return true;
                    }
                }
                catch (System.NotImplementedException)
                {
                    // Skip projects that do not implement FullName
                }
            }
            return false;
        }
    }

    // Helper control to host the tabbed UI
    public class TabbedContentControl : ContentControl
    {
        public TabbedContentControl()
        {
            Content = new Grid
            {
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F3F3F3"),
                Children =
                {
                    new TabControl
                    {
                        Margin = new Thickness(0),
                        BorderThickness = new Thickness(0),
                        Background = System.Windows.Media.Brushes.White,
                        Items =
                        {
                            new TabItem { Header = "Overview", Content = new OverviewTabControl() },
                            new TabItem { Header = "Changes", Content = new ChangesTabControl() },
                            new TabItem { Header = "Deployment", Content = new DeploymentTabControl() }
                            // SettingsTabControl intentionally not referenced
                        }
                    }
                }
            };
        }
    }
}
