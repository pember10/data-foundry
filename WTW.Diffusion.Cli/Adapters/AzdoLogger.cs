using WTW.Diffusion.Core.Abstractions;

namespace WTW.Diffusion.Cli.Adapters;

/// <summary>
/// ILogger implementation that emits Azure DevOps logging commands alongside
/// normal console output. Activate with --azdo flag.
///
/// ADO logging command reference:
/// https://learn.microsoft.com/azure/devops/pipelines/scripts/logging-commands
/// </summary>
internal sealed class AzdoLogger : ILogger
{
    private readonly ConsoleLogger _console = new();

    /// <summary>Informational message — plain stdout, visible in pipeline logs.</summary>
    public void Log(string message)
    {
        _console.Log(message);
    }

    /// <summary>
    /// Error — emits ##[error] so ADO marks the step as failed in the log viewer
    /// and surfaces the message in the pipeline summary.
    /// </summary>
    public void LogError(string message)
    {
        // ADO picks up ##[error] and marks the task red in the log viewer.
        Console.WriteLine($"##[error]{message}");
        _console.LogError(message);
    }

    /// <summary>
    /// Warning — emits ##[warning] so ADO surfaces the message in the pipeline summary.
    /// </summary>
    public void LogWarning(string message)
    {
        Console.WriteLine($"##[warning]{message}");
        _console.LogWarning(message);
    }

    public void LogDebug(string message) => _console.LogDebug(message);

    // -------------------------------------------------------------------------
    // Pipeline variable helpers
    // These write ##vso[task.setvariable] commands that ADO intercepts and
    // stores as pipeline variables, available to all subsequent steps.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets a pipeline variable visible to subsequent steps in the same job.
    /// </summary>
    public static void SetVariable(string name, string value)
        => Console.WriteLine($"##vso[task.setvariable variable={name}]{value}");

    /// <summary>
    /// Sets a pipeline variable and makes it available across jobs (isOutput=true).
    /// Downstream jobs reference it as: $[ dependencies.JobName.outputs['StepName.VarName'] ]
    /// </summary>
    public static void SetOutputVariable(string name, string value)
        => Console.WriteLine($"##vso[task.setvariable variable={name};isOutput=true]{value}");

    /// <summary>
    /// Uploads a markdown file as a pipeline run summary tab.
    /// </summary>
    public static void UploadSummary(string markdownFilePath)
        => Console.WriteLine($"##vso[task.uploadsummary]{markdownFilePath}");

    /// <summary>
    /// Adds a build tag visible on the pipeline run page.
    /// </summary>
    public static void AddBuildTag(string tag)
        => Console.WriteLine($"##vso[build.addbuildtag]{tag}");
}
