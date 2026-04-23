using WTW.Diffusion.Core.Abstractions;

namespace WTW.Diffusion.Cli.Adapters;

/// <summary>
/// ILogger implementation that writes to the console with colour-coded severity levels.
/// </summary>
internal sealed class ConsoleLogger : ILogger
{
    public void Log(string message) => WriteLine(message, ConsoleColor.White);

    public void LogError(string message) => WriteLine($"[ERROR] {message}", ConsoleColor.Red);

    public void LogWarning(string message) => WriteLine($"[WARN]  {message}", ConsoleColor.Yellow);

    public void LogDebug(string message) => WriteLine($"[DEBUG] {message}", ConsoleColor.DarkGray);

    private static void WriteLine(string message, ConsoleColor colour)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.WriteLine(message);
        Console.ForegroundColor = prev;
    }
}
