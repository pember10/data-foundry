using Newtonsoft.Json;
using WTW.Diffusion.Cli.Adapters;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Database;

namespace WTW.Diffusion.Cli.Infrastructure;

/// <summary>
/// Shared logic called by all subcommand handlers.
/// </summary>
internal static class CommandHelpers
{
    internal static bool IsAzureServer(string targetServer) =>
        targetServer.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the migrations path, acquires an Azure SQL token when needed,
    /// and builds the <see cref="ServiceContext"/>.
    /// </summary>
    internal static (ServiceContext ctx, ILogger logger) BuildContext(
        string targetServer,
        string targetDatabase,
        string migrationsPath,
        string? shadowDatabase,
        string? migrationLogSchema,
        bool azdo,
        string? sqlProject,
        string? solutionRoot)
    {
        ILogger logger = azdo ? new AzdoLogger() : new ConsoleLogger();
        migrationsPath = Path.GetFullPath(migrationsPath);

        string? accessToken = null;
        if (IsAzureServer(targetServer))
        {
            logger.Log("Acquiring Azure SQL access token...");
            var authProvider = new AzureSqlAuthenticationProvider();
            accessToken = authProvider.GetAccessToken(targetServer);
        }

        var effectiveSolutionRoot = solutionRoot
            ?? (sqlProject is not null ? Path.GetDirectoryName(migrationsPath) : null);

        var ctx = ServiceContext.Build(
            targetServer, targetDatabase, migrationsPath,
            shadowDatabase, migrationLogSchema, accessToken,
            effectiveSolutionRoot);

        return (ctx, logger);
    }

    /// <summary>
    /// Creates the target database if it does not exist (skipped for Azure SQL)
    /// and ensures the migration log table is present.
    /// </summary>
    internal static void PrepareDatabase(
        ServiceContext ctx,
        string? migrationLogSchema,
        bool isAzure,
        ILogger logger)
    {
        if (!isAzure)
        {
            logger.Log("Ensuring target database...");
            ctx.Repository.CreateDatabaseIfMissing(ctx.TargetDatabase);
        }

        logger.Log("Ensuring migration log table...");
        ctx.Repository.EnsureMigrationLogTable(
            ctx.TargetDatabase,
            migrationLogSchema ?? Path.Combine(AppContext.BaseDirectory, "MigrationLogTableDefinition.sql"));
    }

    /// <summary>
    /// Validates the SQL Server version against the project's DSP or an explicit override.
    /// Advisory only — never throws.
    /// </summary>
    internal static void CheckServerVersion(
        ServiceContext ctx,
        string? sqlProject,
        int? minVersion,
        ILogger logger)
    {
        string? projectFilePath = (ctx.ProjectManager is not null && sqlProject is not null)
            ? ctx.ProjectManager.GetProjectPath(sqlProject)
            : null;

        SqlProjectDspReader.ValidateAndWarn(
            ctx.TargetDatabase, ctx.Repository, logger, minVersion, projectFilePath);
    }

    /// <summary>
    /// Applies all pending migration scripts to the target database.
    /// Returns the number of scripts applied.
    /// </summary>
    internal static int ApplyPendingMigrations(
        ServiceContext ctx,
        bool confirmFirst,
        ILogger logger,
        CancellationToken ct)
    {
        var pending = ctx.ScriptManager.GetPendingMigrations(ctx.TargetDatabase);

        if (pending.Count == 0)
        {
            logger.Log("No pending migrations found.");
            return 0;
        }

        ConfirmIfRequired(pending, confirmFirst, logger);

        foreach (var migration in pending)
        {
            ct.ThrowIfCancellationRequested();
            logger.Log($"Executing {migration.FileName} [{migration.Id}]");
            ctx.ScriptManager.ExecuteMigrationScript(ctx.TargetDatabase, migration);
        }

        logger.Log("Migrations complete.");
        return pending.Count;
    }

    /// <summary>
    /// Recreates the shadow database and computes per-table change counts.
    /// Returns the full summary and the subset of tables that have changes.
    /// </summary>
    internal static (List<TableChangeSummary> summary, List<TableChangeSummary> diffs) RunChangeDetection(
        ServiceContext ctx,
        string configPath,
        ILogger logger,
        CancellationToken ct)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"--config-path not found: {configPath}");

        var config = JsonConvert.DeserializeObject<TrackedTablesConfig>(File.ReadAllText(configPath));

        if (config?.TrackedTables is not { Length: > 0 })
            throw new InvalidOperationException("No TrackedTables found in config.json.");

        logger.Log("Recreating shadow database...");
        ctx.ShadowManager.Recreate(ct);
        logger.Log("Shadow migrations complete.");

        logger.Log("Detecting changes between target and shadow...");
        var summary = ctx.ChangeDetection.GetChangesSummary(
            ctx.TargetDatabase, ctx.ShadowDatabase, config.TrackedTables.ToList());

        return (summary, summary.Where(s => s.HasChanges).ToList());
    }

    /// <summary>
    /// Generates a migration script from the diff between target and shadow,
    /// logs the new GUID to the migration log, and returns the file path.
    /// </summary>
    internal static string GenerateMigrationScript(
        ServiceContext ctx,
        List<string> changedTables,
        string outputDir,
        string scriptName,
        ILogger logger)
    {
        string filePath = ctx.ScriptGenerator.GenerateMigrationScript(
            ctx.TargetDatabase, ctx.ShadowDatabase, changedTables, outputDir, scriptName);

        logger.Log($"Generated migration script: {filePath}");

        var info = ctx.ScriptManager.GetMigrationInfoFromFile(filePath);
        if (info is not null)
        {
            logger.Log("Logging newly generated migration script to migration log...");
            ctx.ScriptManager.ExecuteMigrationScript(ctx.TargetDatabase, info, skipExecution: true);
        }

        return filePath;
    }

    /// <summary>
    /// Adds a generated script to the configured <c>.sqlproj</c>.
    /// Exits with code 3 if the project cannot be resolved.
    /// </summary>
    internal static void AddScriptToProject(
        ServiceContext ctx,
        string filePath,
        string? sqlProject,
        ILogger logger)
    {
        if (sqlProject is null) return;

        if (ctx.ProjectManager is null)
        {
            logger.LogError($"SQL project '{sqlProject}' could not be resolved. Use --solution-root to specify the search directory.");
            Environment.Exit(3);
            return;
        }

        string? projectPath = ctx.ProjectManager.GetProjectPath(sqlProject);
        if (projectPath is null)
        {
            logger.LogError($"SQL project '{sqlProject}' not found. Use --solution-root to specify the search directory.");
            Environment.Exit(3);
            return;
        }

        string folderPath = ctx.ProjectManager.GetRelativeFolderPath(projectPath, filePath) ?? string.Empty;
        ctx.ProjectManager.AddFileToProject(sqlProject, filePath, folderPath);
        logger.Log($"Added script to project '{sqlProject}'.");
    }

    /// <summary>Publishes ADO pipeline variables and summary tab when running in --azdo mode.</summary>
    internal static void PublishAzdoOutput(
        bool azdo,
        int pendingCount,
        List<TableChangeSummary>? changes,
        string? scriptPath)
    {
        if (azdo)
            PipelineOutput.Publish(pendingCount, changes, scriptPath, Path.GetTempPath());
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static void ConfirmIfRequired(List<MigrationInfo> pending, bool confirmFirst, ILogger logger)
    {
        if (!confirmFirst) return;

        logger.Log("Pending migrations:");
        pending.ForEach(m => logger.Log($"  -> {m.FileName} [{m.Id}]"));
        Console.Write("Execute these migrations? (y/n): ");

        if (Console.ReadLine()?.Trim().ToLowerInvariant() != "y")
            throw new OperationCanceledException("Target migrations aborted by user.");
    }
}
