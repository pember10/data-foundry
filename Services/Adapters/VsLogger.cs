using data_foundry.Options;
using Microsoft.VisualStudio.Shell;
using WTW.Diffusion.Core.Abstractions;

namespace data_foundry.Services.Adapters
{
    /// <summary>
    /// ILogger adapter for the Visual Studio host.
    /// Wraps OutputWindowLogger and respects the VerboseLogging option for debug messages.
    /// </summary>
    public class VsLogger : ILogger
    {
        private readonly bool _verboseLogging;

        public VsLogger(DataFoundryOptions options)
        {
            _verboseLogging = options?.VerboseLogging ?? false;
        }

        public void Log(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowLogger.Log(message);
        }

        public void LogError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowLogger.LogError(message);
        }

        public void LogWarning(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowLogger.LogWarning(message);
        }

        public void LogDebug(string message)
        {
            if (!_verboseLogging)
                return;

            ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowLogger.Log($"DEBUG: {message}");
        }
    }
}
