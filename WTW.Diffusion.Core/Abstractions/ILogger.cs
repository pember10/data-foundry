namespace WTW.Diffusion.Core.Abstractions
{
    /// <summary>
    /// Abstraction for logging functionality.
    /// Implementations can target console, file, Visual Studio Output window, etc.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        void Log(string message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        void LogError(string message);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        void LogWarning(string message);

        /// <summary>
        /// Logs a debug message (may be filtered in production).
        /// </summary>
        void LogDebug(string message);
    }
}
