using data_foundry.Options;
using Microsoft.VisualStudio.Shell;
using WTW.Diffusion.Core.Abstractions;

namespace data_foundry.Services.Adapters
{
    /// <summary>
    /// ILogger adapter for the Visual Studio host.
    /// Thread-safe: marshals all writes to the UI thread via JoinableTaskFactory.
    /// Replaces the per-call lambda logging pattern in the View code-behinds.
    /// </summary>
    public class VsLogger : ILogger
    {
        private readonly bool _verboseLogging;

        public VsLogger(DataFoundryOptions options = null)
        {
            _verboseLogging = options?.VerboseLogging ?? false;
        }

        public void Log(string message)      => Marshal(() => OutputWindowLogger.Log(message));
        public void LogError(string message) => Marshal(() => OutputWindowLogger.LogError(message));
        public void LogWarning(string message) => Marshal(() => OutputWindowLogger.LogWarning(message));

        public void LogDebug(string message)
        {
            if (_verboseLogging)
                Marshal(() => OutputWindowLogger.Log($"DEBUG: {message}"));
        }

        private static void Marshal(System.Action action)
        {
            if (ThreadHelper.CheckAccess())
            {
                action();
            }
            else
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    action();
                });
            }
        }
    }
}
