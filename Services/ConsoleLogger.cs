using System;
using WTW.Diffusion.Core.Abstractions;

namespace data_foundry.Services
{
    /// <summary>
    /// Fallback ILogger that writes to Console. Used when no logger is injected
    /// (e.g. in the secondary SqlMigrationOrchestrator constructor used by tests/CLI).
    /// </summary>
    internal sealed class ConsoleLogger : ILogger
    {
        public static readonly ConsoleLogger Instance = new ConsoleLogger();

        public void Log(string message)      => Console.WriteLine(message);
        public void LogError(string message) => Console.Error.WriteLine($"[ERROR] {message}");
        public void LogWarning(string message) => Console.WriteLine($"[WARN]  {message}");
        public void LogDebug(string message) => System.Diagnostics.Debug.WriteLine(message);
    }
}
