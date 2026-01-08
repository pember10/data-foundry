using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace data_foundry.Services
{
    /// <summary>
    /// Logger that writes to Visual Studio Output window.
    /// </summary>
    public class OutputWindowLogger
    {
        private static readonly Guid _dataFoundryPaneGuid = new Guid("A7F3C8D2-9B4E-4C1A-8D3E-2F5A6B7C8D9E");
        private static IVsOutputWindowPane _outputPane;
        private static readonly object _lock = new object();

        /// <summary>
        /// Writes a message to the Data Foundry output pane.
        /// </summary>
        public static void Log(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                var pane = GetOrCreatePane();
                if (pane != null)
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    pane.OutputStringThreadSafe($"[{timestamp}] {message}\r\n");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes an error message to the Data Foundry output pane.
        /// </summary>
        public static void LogError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log($"ERROR: {message}");
        }

        /// <summary>
        /// Writes a warning message to the Data Foundry output pane.
        /// </summary>
        public static void LogWarning(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log($"WARNING: {message}");
        }

        /// <summary>
        /// Clears the Data Foundry output pane.
        /// </summary>
        public static void Clear()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                var pane = GetOrCreatePane();
                pane?.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear output window: {ex.Message}");
            }
        }

        /// <summary>
        /// Activates and shows the Data Foundry output pane.
        /// </summary>
        public static void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            
            try
            {
                var pane = GetOrCreatePane();
                pane?.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show output window: {ex.Message}");
            }
        }

        private static IVsOutputWindowPane GetOrCreatePane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            lock (_lock)
            {
                if (_outputPane != null)
                    return _outputPane;

                var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow == null)
                    return null;

                // Create a local copy to use with ref
                var paneGuid = _dataFoundryPaneGuid;

                // Try to get existing pane
                var hr = outputWindow.GetPane(ref paneGuid, out _outputPane);

                // Create pane if it doesn't exist
                if (ErrorHandler.Failed(hr) || _outputPane == null)
                {
                    paneGuid = _dataFoundryPaneGuid;
                    outputWindow.CreatePane(
                        ref paneGuid,
                        "Data Foundry",
                        fInitVisible: 1,
                        fClearWithSolution: 0);

                    paneGuid = _dataFoundryPaneGuid;
                    outputWindow.GetPane(ref paneGuid, out _outputPane);
                }

                return _outputPane;
            }
        }
    }
}
