using System;
using System.Windows;
using System.Windows.Controls;
using data_foundry.Views.Controls;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;
using WTW.Diffusion.Core.Services;

namespace data_foundry
{
    public partial class DataFoundryToolWindowControl : UserControl
    {
        private DTE _environment;
        private SolutionEvents _solutionEvents;

        public DataFoundryToolWindowControl()
        {
            InitializeComponent();

            // Defer initialization to avoid blocking the constructor
            Loaded += OnLoaded;
        }

        // Change the event handler signature to match RoutedEventHandler (void return type)
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Only initialize once
            Loaded -= OnLoaded;

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                InitializeDteEvents();
                UpdateContent();

                // If a solution is already open when the tool window loads, treat it
                // the same as OnSolutionOpened — the event won't fire retroactively.
                bool solutionOpen = _environment?.Solution?.IsOpen == true;
                System.Diagnostics.Debug.WriteLine(
                    $"[WTW] OnLoaded: environment={(_environment == null ? "NULL" : "OK")}, solutionOpen={solutionOpen}");

                if (solutionOpen)
                {
                    OnSolutionOpened();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error metadata automation tool window: {ex}");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _ = MessageBox.Show($"Failed to initialize WTW Diffusion tool: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeDteEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _environment = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (_environment != null && _environment.Events != null)
            {
                _solutionEvents = _environment.Events.SolutionEvents;
                _solutionEvents.Opened += OnSolutionOpened;
                _solutionEvents.AfterClosing += SolutionOrProjectChanged_NoArgs;
                _solutionEvents.ProjectAdded += SolutionOrProjectChanged_Project;
                _solutionEvents.ProjectRemoved += SolutionOrProjectChanged_Project;
            }
        }

        private void OnSolutionOpened()
        {
            // Update UI first
            _ = UpdateContentAsync();

            // Fire auto-refresh on a background thread so we don't block the solution-open event.
            // GetServiceAsync ensures the package is fully initialised before we read its options,
            // which fixes the race between BackgroundLoad and the tool window Loaded event.
            _ = TryAutoRefreshAsync();
        }

        private async Task TryAutoRefreshAsync()
        {
            try
            {
                // Brief yield so the UI thread can finish rendering before we start work
                await Task.Yield();

                var package = await AsyncServiceProvider.GlobalProvider
                    .GetServiceAsync(typeof(data_foundryPackage)) as data_foundryPackage
                    ?? data_foundryPackage.Instance;

                System.Diagnostics.Debug.WriteLine(
                    $"[WTW] TryAutoRefreshAsync: package={(package == null ? "NULL" : "OK")}");

                if (package == null)
                    return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var options = (Options.DataFoundryOptions)package.GetDialogPage(typeof(Options.DataFoundryOptions));

                System.Diagnostics.Debug.WriteLine(
                    $"[WTW] TryAutoRefreshAsync: AutoRefresh={options.AutoRefresh}");

                if (options.AutoRefresh)
                    _ = TriggerAutoRefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WTW] TryAutoRefreshAsync failed: {ex.Message}");
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

            MainContent.Content = HasSqlProjectInSolution() ? new TabbedContentControl() : (object)new NoSqlProjectMessageControl();
        }

        private bool HasSqlProjectInSolution()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_environment?.Solution == null || _environment.Solution.Projects == null)
            {
                return false;
            }

            foreach (Project project in _environment.Solution.Projects)
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

        private async Task TriggerAutoRefreshAsync()
        {
            try
            {
                // Wait a bit for solution to fully load
                await Task.Delay(2000);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                Services.OutputWindowLogger.Clear();
                Services.OutputWindowLogger.Show();
                Services.OutputWindowLogger.Log("=== Auto-Refresh: Detecting Database Changes ===");

                // Create orchestrator on UI thread
                SqlMigrationOrchestrator orchestrator = Services.SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

                // Now run detection on background thread
                await Task.Run(() =>
                {
                    _ = orchestrator.DetectAndHandleChanges(action: null);
                });

                Services.OutputWindowLogger.Log("=== Auto-Refresh Complete ===");
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Services.OutputWindowLogger.LogError($"Auto-refresh failed: {ex.Message}");
            }
        }
    }
}
