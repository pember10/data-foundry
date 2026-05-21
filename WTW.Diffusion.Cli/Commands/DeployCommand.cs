using System.CommandLine;
using WTW.Diffusion.Cli.Infrastructure;

namespace WTW.Diffusion.Cli.Commands;

/// <summary>
/// <c>deploy</c> — applies all pending migration scripts to the target database.
/// </summary>
internal static class DeployCommand
{
    internal static Command Build()
    {
        var targetDatabaseOpt    = CliOptions.TargetDatabase();
        var targetServerOpt      = CliOptions.TargetServer();
        var migrationsPathOpt    = CliOptions.MigrationsPath();
        var confirmOpt           = CliOptions.ConfirmTargetMigration();
        var migrationLogSchemaOpt = CliOptions.MigrationLogSchema();
        var azdoOpt              = CliOptions.Azdo();
        var sqlProjectOpt        = CliOptions.SqlProject();
        var solutionRootOpt      = CliOptions.SolutionRoot();
        var minSqlVersionOpt     = CliOptions.MinSqlVersion();

        var cmd = new Command("deploy",
            "Apply all pending migration scripts to the target database.")
        {
            targetDatabaseOpt, targetServerOpt, migrationsPathOpt,
            confirmOpt, migrationLogSchemaOpt,
            azdoOpt, sqlProjectOpt, solutionRootOpt, minSqlVersionOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var targetDatabase    = context.ParseResult.GetValueForOption(targetDatabaseOpt)!;
            var targetServer      = context.ParseResult.GetValueForOption(targetServerOpt)!;
            var migrationsPath    = context.ParseResult.GetValueForOption(migrationsPathOpt)!;
            var confirm           = context.ParseResult.GetValueForOption(confirmOpt);
            var migrationLogSchema = context.ParseResult.GetValueForOption(migrationLogSchemaOpt);
            var azdo              = context.ParseResult.GetValueForOption(azdoOpt);
            var sqlProject        = context.ParseResult.GetValueForOption(sqlProjectOpt);
            var solutionRoot      = context.ParseResult.GetValueForOption(solutionRootOpt);
            var minSqlVersion     = context.ParseResult.GetValueForOption(minSqlVersionOpt);
            var ct                = context.GetCancellationToken();

            var (ctx, logger) = CommandHelpers.BuildContext(
                targetServer, targetDatabase, migrationsPath,
                shadowDatabase: null, migrationLogSchema, azdo, sqlProject, solutionRoot);

            try
            {
                CommandHelpers.PrepareDatabase(ctx, migrationLogSchema,
                    CommandHelpers.IsAzureServer(targetServer), logger);

                CommandHelpers.CheckServerVersion(ctx, sqlProject, minSqlVersion, logger);

                int applied = CommandHelpers.ApplyPendingMigrations(ctx, confirm, logger, ct);

                CommandHelpers.PublishAzdoOutput(azdo, applied, null, null);
            }
            catch (OperationCanceledException ex) { logger.LogError(ex.Message); Environment.Exit(1); }
            catch (Exception ex)                  { logger.LogError(ex.Message); Environment.Exit(2); }
            finally { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); }
        });

        return cmd;
    }
}
