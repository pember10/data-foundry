using System.CommandLine;
using WTW.Diffusion.Cli.Infrastructure;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Cli.Commands;

/// <summary>
/// <c>detect-changes</c> — rebuilds the shadow database, diffs tracked tables against the target,
/// and applies the chosen action (Revert | Migrate | Cancel).
/// </summary>
internal static class DetectChangesCommand
{
    internal static Command Build()
    {
        var targetDatabaseOpt     = CliOptions.TargetDatabase();
        var targetServerOpt       = CliOptions.TargetServer();
        var migrationsPathOpt     = CliOptions.MigrationsPath();
        var shadowDatabaseOpt     = CliOptions.ShadowDatabase();
        var configPathOpt         = CliOptions.ConfigPath();
        var actionOpt             = CliOptions.Action();
        var outputMigrationDirOpt = CliOptions.OutputMigrationDir();
        var scriptNameOpt         = CliOptions.ScriptName();
        var migrationLogSchemaOpt = CliOptions.MigrationLogSchema();
        var azdoOpt               = CliOptions.Azdo();
        var sqlProjectOpt         = CliOptions.SqlProject();
        var solutionRootOpt       = CliOptions.SolutionRoot();
        var minSqlVersionOpt      = CliOptions.MinSqlVersion();

        var cmd = new Command("detect-changes",
            "Rebuild the shadow database, detect data changes in tracked tables, " +
            "and apply the chosen action (Revert | Migrate | Cancel).")
        {
            targetDatabaseOpt, targetServerOpt, migrationsPathOpt,
            shadowDatabaseOpt, configPathOpt, actionOpt,
            outputMigrationDirOpt, scriptNameOpt, migrationLogSchemaOpt,
            azdoOpt, sqlProjectOpt, solutionRootOpt, minSqlVersionOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var targetDatabase     = context.ParseResult.GetValueForOption(targetDatabaseOpt)!;
            var targetServer       = context.ParseResult.GetValueForOption(targetServerOpt)!;
            var migrationsPath     = context.ParseResult.GetValueForOption(migrationsPathOpt)!;
            var shadowDatabase     = context.ParseResult.GetValueForOption(shadowDatabaseOpt);
            var configPath         = context.ParseResult.GetValueForOption(configPathOpt)!;
            var action             = context.ParseResult.GetValueForOption(actionOpt);
            var outputMigrationDir = context.ParseResult.GetValueForOption(outputMigrationDirOpt);
            var scriptName         = context.ParseResult.GetValueForOption(scriptNameOpt);
            var migrationLogSchema = context.ParseResult.GetValueForOption(migrationLogSchemaOpt);
            var azdo               = context.ParseResult.GetValueForOption(azdoOpt);
            var sqlProject         = context.ParseResult.GetValueForOption(sqlProjectOpt);
            var solutionRoot       = context.ParseResult.GetValueForOption(solutionRootOpt);
            var minSqlVersion      = context.ParseResult.GetValueForOption(minSqlVersionOpt);
            var ct                 = context.GetCancellationToken();

            var (ctx, logger) = CommandHelpers.BuildContext(
                targetServer, targetDatabase, migrationsPath,
                shadowDatabase, migrationLogSchema, azdo, sqlProject, solutionRoot);

            try
            {
                CommandHelpers.PrepareDatabase(ctx, migrationLogSchema,
                    CommandHelpers.IsAzureServer(targetServer), logger);

                CommandHelpers.CheckServerVersion(ctx, sqlProject, minSqlVersion, logger);

                var (summary, diffs) = CommandHelpers.RunChangeDetection(ctx, configPath, logger, ct);

                if (diffs.Count == 0)
                {
                    logger.Log("No changes detected.");
                    CommandHelpers.PublishAzdoOutput(azdo, 0, summary, null);
                    return;
                }

                LogDiffs(diffs, logger);

                var resolvedAction = ResolveAction(action, ctx.TargetDatabase, logger);
                var outputDir = outputMigrationDir is not null
                    ? Path.GetFullPath(outputMigrationDir)
                    : ctx.MigrationsPath;

                var changedTables = diffs.Select(d => d.Table).ToList();
                var scriptPath = ApplyAction(
                    ctx, resolvedAction, changedTables, outputDir, scriptName, sqlProject, logger);

                CommandHelpers.PublishAzdoOutput(azdo, 0, summary, scriptPath);
            }
            catch (OperationCanceledException ex) { logger.LogError(ex.Message); Environment.Exit(1); }
            catch (Exception ex)                  { logger.LogError(ex.Message); Environment.Exit(2); }
            finally { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); }
        });

        return cmd;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static void LogDiffs(List<TableChangeSummary> diffs, WTW.Diffusion.Core.Abstractions.ILogger logger)
    {
        logger.Log("Changes detected:");
        foreach (var d in diffs)
            logger.Log($"  {d.Table,-40}  +{d.Inserts,6}  ~{d.Updates,6}  -{d.Deletes,6}");
    }

    private static string ResolveAction(string? action, string targetDatabase, WTW.Diffusion.Core.Abstractions.ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(action))
            return action;

        logger.Log(string.Empty);
        logger.Log("How would you like to handle these changes?");
        logger.Log("  1) Generate migration script");
        logger.Log($"  2) Revert changes in {targetDatabase}");
        logger.Log("  3) Cancel/Exit");

        while (true)
        {
            Console.Write("> ");
            var ans = Console.ReadLine()?.Trim();
            var resolved = ans switch { "1" => "Migrate", "2" => "Revert", "3" => "Cancel", _ => null };
            if (resolved is not null) return resolved;
            logger.LogWarning("Please enter 1, 2, or 3.");
        }
    }

    private static string? ApplyAction(
        ServiceContext ctx,
        string action,
        List<string> changedTables,
        string outputDir,
        string? scriptName,
        string? sqlProject,
        WTW.Diffusion.Core.Abstractions.ILogger logger)
    {
        switch (action)
        {
            case "Revert":
                logger.Log($"Reverting changes in {ctx.TargetDatabase}...");
                ctx.ChangeDetection.RevertChanges(ctx.TargetDatabase, ctx.ShadowDatabase, changedTables);
                logger.Log("Revert complete.");
                return null;

            case "Migrate":
                var resolvedName = PromptScriptName(scriptName, logger);
                var filePath = CommandHelpers.GenerateMigrationScript(
                    ctx, changedTables, outputDir, resolvedName, logger);
                CommandHelpers.AddScriptToProject(ctx, filePath, sqlProject, logger);
                return filePath;

            default: // Cancel
                logger.Log("Action cancelled. No changes applied.");
                return null;
        }
    }

    private static string PromptScriptName(string? scriptName, WTW.Diffusion.Core.Abstractions.ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(scriptName))
        {
            logger.Log($"Using provided script name: {scriptName}");
            return scriptName;
        }

        Console.Write("Enter migration script name (no extension, e.g. 001_US123456_Description): ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }
}
