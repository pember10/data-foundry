using System.CommandLine;
using WTW.Diffusion.Cli.Infrastructure;

namespace WTW.Diffusion.Cli.Commands;

/// <summary>
/// <c>generate-script</c> — rebuilds the shadow database, detects data changes,
/// and generates a migration script without prompting for an action (Migrate is implied).
/// </summary>
internal static class GenerateScriptCommand
{
    internal static Command Build()
    {
        var targetDatabaseOpt     = CliOptions.TargetDatabase();
        var targetServerOpt       = CliOptions.TargetServer();
        var migrationsPathOpt     = CliOptions.MigrationsPath();
        var shadowDatabaseOpt     = CliOptions.ShadowDatabase();
        var configPathOpt         = CliOptions.ConfigPath();
        var scriptNameOpt         = CliOptions.ScriptName();
        var outputMigrationDirOpt = CliOptions.OutputMigrationDir();
        var migrationLogSchemaOpt = CliOptions.MigrationLogSchema();
        var azdoOpt               = CliOptions.Azdo();
        var sqlProjectOpt         = CliOptions.SqlProject();
        var solutionRootOpt       = CliOptions.SolutionRoot();
        var minSqlVersionOpt      = CliOptions.MinSqlVersion();

        var cmd = new Command("generate-script",
            "Rebuild the shadow database, detect data changes, and generate a migration script " +
            "(equivalent to detect-changes --action Migrate).")
        {
            targetDatabaseOpt, targetServerOpt, migrationsPathOpt,
            shadowDatabaseOpt, configPathOpt, scriptNameOpt,
            outputMigrationDirOpt, migrationLogSchemaOpt,
            azdoOpt, sqlProjectOpt, solutionRootOpt, minSqlVersionOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var targetDatabase     = context.ParseResult.GetValueForOption(targetDatabaseOpt)!;
            var targetServer       = context.ParseResult.GetValueForOption(targetServerOpt)!;
            var migrationsPath     = context.ParseResult.GetValueForOption(migrationsPathOpt)!;
            var shadowDatabase     = context.ParseResult.GetValueForOption(shadowDatabaseOpt);
            var configPath         = context.ParseResult.GetValueForOption(configPathOpt)!;
            var scriptName         = context.ParseResult.GetValueForOption(scriptNameOpt);
            var outputMigrationDir = context.ParseResult.GetValueForOption(outputMigrationDirOpt);
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
                    logger.Log("No changes detected. Nothing to generate.");
                    CommandHelpers.PublishAzdoOutput(azdo, 0, summary, null);
                    return;
                }

                logger.Log("Changes detected:");
                foreach (var d in diffs)
                    logger.Log($"  {d.Table,-40}  +{d.Inserts,6}  ~{d.Updates,6}  -{d.Deletes,6}");

                var resolvedName = ResolveScriptName(scriptName, logger);
                var outputDir = outputMigrationDir is not null
                    ? Path.GetFullPath(outputMigrationDir)
                    : ctx.MigrationsPath;

                var changedTables = diffs.Select(d => d.Table).ToList();
                var filePath = CommandHelpers.GenerateMigrationScript(
                    ctx, changedTables, outputDir, resolvedName, logger);

                CommandHelpers.AddScriptToProject(ctx, filePath, sqlProject, logger);
                CommandHelpers.PublishAzdoOutput(azdo, 0, summary, filePath);
            }
            catch (OperationCanceledException ex) { logger.LogError(ex.Message); Environment.Exit(1); }
            catch (Exception ex)                  { logger.LogError(ex.Message); Environment.Exit(2); }
            finally { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); }
        });

        return cmd;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static string ResolveScriptName(string? scriptName, WTW.Diffusion.Core.Abstractions.ILogger logger)
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
