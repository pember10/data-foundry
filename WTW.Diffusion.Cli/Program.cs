using System.CommandLine;
using Newtonsoft.Json;
using WTW.Diffusion.Cli.Adapters;
using WTW.Diffusion.Cli.Commands;
using WTW.Diffusion.Cli.Infrastructure;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Database;

// ?? Options — mirror SqlMetadataAutomation.ps1 parameters exactly ??????????

var targetDatabaseOpt = new Option<string>(
    ["--target-database", "--TargetDatabase"],
    "Name of the target database (e.g. AppDb).")
{ IsRequired = true };

var targetServerOpt = new Option<string>(
    ["--target-server", "--TargetServer"],
    "SQL Server instance or hostname (e.g. .\\SQLEXPRESS or myserver.database.windows.net).")
{ IsRequired = true };

var migrationsPathOpt = new Option<string>(
    ["--migrations-path", "--MigrationsPath"],
    "Path containing migration script files (searched recursively).")
{ IsRequired = true };

var confirmTargetMigrationOpt = new Option<bool>(
    ["--confirm-target-migration", "--ConfirmTargetMigration"],
    "If set, prompt for confirmation before executing pending migrations on the target.");

var detectChangesOpt = new Option<bool>(
    ["--detect-changes", "--DetectChanges"],
    "If set, recreates the shadow database and compares tracked tables against the target.");

var actionOpt = new Option<string?>(
    ["--action", "--Action"],
    "Action after change detection: Revert | Migrate | Cancel. Prompts interactively if omitted.");
actionOpt.AddValidator(r =>
{
    var v = r.GetValueOrDefault<string?>();
    if (v is not null && v is not "Revert" and not "Migrate" and not "Cancel")
        r.ErrorMessage = "--action must be Revert, Migrate, or Cancel.";
});

var configPathOpt = new Option<string>(
    ["--config-path", "--ConfigPath"],
    description: "Path to config.json containing the TrackedTables array.",
    getDefaultValue: () => Path.Combine(Directory.GetCurrentDirectory(), "config.json"));

var outputMigrationDirOpt = new Option<string?>(
    ["--output-migration-dir", "--OutputMigrationDir"],
    "Root path for the generated migration script when --action Migrate. Defaults to --migrations-path.");

var scriptNameOpt = new Option<string?>(
    ["--script-name", "--ScriptName"],
    "Name for the generated migration script when --action Migrate. Prompts interactively if omitted.");

var shadowDatabaseOpt = new Option<string?>(
    ["--shadow-database", "--ShadowDatabase"],
    "Shadow database name. Defaults to {TargetDatabase}_Shadow.");

var migrationLogSchemaOpt = new Option<string?>(
    ["--migration-log-schema", "--MigrationLogSchema"],
    "Path to MigrationLogTableDefinition.sql. Defaults to the file beside the executable.");

// ?? Root command ????????????????????????????????????????????????????????????

var root = new RootCommand(
    "WTW Diffusion CLI — drop-in replacement for SqlMetadataAutomation.ps1.")
{
    targetDatabaseOpt,
    targetServerOpt,
    migrationsPathOpt,
    confirmTargetMigrationOpt,
    detectChangesOpt,
    actionOpt,
    configPathOpt,
    outputMigrationDirOpt,
    scriptNameOpt,
    shadowDatabaseOpt,
    migrationLogSchemaOpt,
    InitCommand.Build()
};

root.SetHandler(async (context) =>
{
    var targetDatabase         = context.ParseResult.GetValueForOption(targetDatabaseOpt)!;
    var targetServer           = context.ParseResult.GetValueForOption(targetServerOpt)!;
    var migrationsPath         = context.ParseResult.GetValueForOption(migrationsPathOpt)!;
    var confirmTargetMigration = context.ParseResult.GetValueForOption(confirmTargetMigrationOpt);
    var detectChanges          = context.ParseResult.GetValueForOption(detectChangesOpt);
    var action                 = context.ParseResult.GetValueForOption(actionOpt);
    var configPath             = context.ParseResult.GetValueForOption(configPathOpt)!;
    var outputMigrationDir     = context.ParseResult.GetValueForOption(outputMigrationDirOpt);
    var scriptName             = context.ParseResult.GetValueForOption(scriptNameOpt);
    var shadowDatabase         = context.ParseResult.GetValueForOption(shadowDatabaseOpt);
    var migrationLogSchema     = context.ParseResult.GetValueForOption(migrationLogSchemaOpt);

    var logger = new ConsoleLogger();

    try
    {
        migrationsPath    = Path.GetFullPath(migrationsPath);
        outputMigrationDir = outputMigrationDir is not null
            ? Path.GetFullPath(outputMigrationDir)
            : migrationsPath;

        // ?? Azure SQL token (mirrors Get-AzureSqlAccessToken) ?????????????
        string? accessToken = null;
        if (targetServer.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            logger.Log("Acquiring Azure SQL access token...");
            var authProvider = new AzureSqlAuthenticationProvider();
            accessToken = authProvider.GetAccessToken(targetServer);
        }

        var ctx = ServiceContext.Build(
            targetServer, targetDatabase, migrationsPath,
            shadowDatabase, migrationLogSchema, accessToken);

        // ?? Target migration execution ?????????????????????????????????????
        if (accessToken is null)
        {
            logger.Log("Ensuring target database...");
            ctx.Repository.CreateDatabaseIfMissing(ctx.TargetDatabase);
        }

        logger.Log("Ensuring migration log table...");
        ctx.Repository.EnsureMigrationLogTable(
            ctx.TargetDatabase,
            migrationLogSchema ?? Path.Combine(AppContext.BaseDirectory, "MigrationLogTableDefinition.sql"));

        var pending = ctx.ScriptManager.GetPendingMigrations(ctx.TargetDatabase);

        if (pending.Count == 0)
        {
            logger.Log("No pending migrations found.");
        }
        else
        {
            if (confirmTargetMigration)
            {
                logger.Log("Pending migrations:");
                pending.ForEach(m => logger.Log($"  -> {m.FileName} [{m.Id}]"));
                Console.Write("Execute these migrations? (y/n): ");
                var resp = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (resp != "y")
                    throw new OperationCanceledException("Target migrations aborted by user.");
            }

            foreach (var migration in pending)
            {
                logger.Log($"Executing {migration.FileName} [{migration.Id}]");
                ctx.ScriptManager.ExecuteMigrationScript(ctx.TargetDatabase, migration);
            }

            logger.Log("Migrations complete.");
        }

        // ?? Change detection ???????????????????????????????????????????????
        if (!detectChanges)
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            return;
        }

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"ConfigPath not found: {configPath}");

        var config = JsonConvert.DeserializeObject<TrackedTablesConfig>(
            File.ReadAllText(configPath));

        if (config?.TrackedTables is not { Length: > 0 })
        {
            logger.LogWarning("No TrackedTables found in config.");
            return;
        }

        // Always do a full recreate — matches PS behaviour
        logger.Log("Recreating shadow database...");
        ctx.ShadowManager.Recreate();
        logger.Log("Shadow migrations complete.");

        logger.Log("Detecting changes between target and shadow...");
        var summary = ctx.ChangeDetection.GetChangesSummary(
            ctx.TargetDatabase, ctx.ShadowDatabase,
            config.TrackedTables.ToList());

        var diffs = summary.Where(s => s.HasChanges).ToList();

        if (diffs.Count == 0)
        {
            logger.Log("No changes detected.");
            return;
        }

        logger.Log("Changes detected:");
        foreach (var d in diffs)
            logger.Log($"  {d.Table,-40}  +{d.Inserts,6}  ~{d.Updates,6}  -{d.Deletes,6}");

        // Resolve action — prompt if not supplied (mirrors Get-DetectChangesAction)
        if (string.IsNullOrWhiteSpace(action))
        {
            logger.Log(string.Empty);
            logger.Log("How would you like to handle these changes?");
            logger.Log("  1) Generate migration script");
            logger.Log($"  2) Revert changes in {ctx.TargetDatabase}");
            logger.Log("  3) Cancel/Exit");

            while (true)
            {
                Console.Write("> ");
                var ans = Console.ReadLine()?.Trim();
                action = ans switch { "1" => "Migrate", "2" => "Revert", "3" => "Cancel", _ => null };
                if (action is not null) break;
                logger.LogWarning("Please enter 1, 2, or 3.");
            }
        }

        var changedTables = diffs.Select(d => d.Table).ToList();

        switch (action)
        {
            case "Revert":
                logger.Log($"Reverting changes in {ctx.TargetDatabase}...");
                ctx.ChangeDetection.RevertChanges(ctx.TargetDatabase, ctx.ShadowDatabase, changedTables);
                logger.Log("Revert complete.");
                break;

            case "Migrate":
                if (string.IsNullOrWhiteSpace(scriptName))
                {
                    Console.Write("Enter migration script name (no extension, e.g. 001_US123456_Description): ");
                    scriptName = Console.ReadLine()?.Trim();
                }
                else
                {
                    logger.Log($"Using provided script name: {scriptName}");
                }

                string filePath = ctx.ScriptGenerator.GenerateMigrationScript(
                    ctx.TargetDatabase, ctx.ShadowDatabase,
                    changedTables, outputMigrationDir, scriptName!);

                logger.Log($"Generated migration script: {filePath}");

                var migrationInfo = ctx.ScriptManager.GetMigrationInfoFromFile(filePath);
                if (migrationInfo is not null)
                {
                    logger.Log("Logging newly generated migration script to migration log...");
                    ctx.ScriptManager.ExecuteMigrationScript(ctx.TargetDatabase, migrationInfo, skipExecution: true);
                }
                break;

            case "Cancel":
                logger.Log("Action cancelled. No changes applied.");
                break;
        }
    }
    catch (OperationCanceledException ex)
    {
        new ConsoleLogger().LogError(ex.Message);
        Environment.Exit(1);
    }
    catch (Exception ex)
    {
        new ConsoleLogger().LogError(ex.Message);
        Environment.Exit(2);
    }
    finally
    {
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
    }
});

return await root.InvokeAsync(args);

// ?? Local config model (mirrors PS config.json) ????????????????????????????
internal sealed class TrackedTablesConfig
{
    [JsonProperty("TrackedTables")]
    public string[] TrackedTables { get; set; } = [];
}
