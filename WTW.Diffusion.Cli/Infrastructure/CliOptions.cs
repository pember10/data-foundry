using System.CommandLine;

namespace WTW.Diffusion.Cli.Infrastructure;

/// <summary>
/// Factory methods for CLI options. Each call returns a new <see cref="Option{T}"/> instance
/// with the same definition, so the same option can be added to multiple commands without
/// conflicts in System.CommandLine's internal registration.
/// </summary>
internal static class CliOptions
{
    internal static Option<string> TargetDatabase() => new(
        ["--target-database", "--TargetDatabase"],
        "Name of the target database (e.g. AppDb).")
        { IsRequired = true };

    internal static Option<string> TargetServer() => new(
        ["--target-server", "--TargetServer"],
        "SQL Server instance or hostname (e.g. .\\SQLEXPRESS or myserver.database.windows.net).")
        { IsRequired = true };

    internal static Option<string> MigrationsPath() => new(
        ["--migrations-path", "--MigrationsPath"],
        "Path containing migration script files (searched recursively).")
        { IsRequired = true };

    internal static Option<bool> ConfirmTargetMigration() => new(
        ["--confirm-target-migration", "--ConfirmTargetMigration"],
        "Prompt for confirmation before executing pending migrations on the target.");

    internal static Option<string?> ShadowDatabase() => new(
        ["--shadow-database", "--ShadowDatabase"],
        "Shadow database name. Defaults to {TargetDatabase}_Shadow.");

    internal static Option<string?> MigrationLogSchema() => new(
        ["--migration-log-schema", "--MigrationLogSchema"],
        "Path to MigrationLogTableDefinition.sql. Defaults to the file beside the executable.");

    internal static Option<bool> Azdo() => new(
        ["--azdo"],
        "Emit Azure DevOps logging commands (##vso[...]) and publish a pipeline summary tab.");

    internal static Option<string?> SqlProject() => new(
        ["--sql-project", "--SqlProject"],
        "Name of the .sqlproj to update after generating a migration script (e.g. Api.Db).");

    internal static Option<string?> SolutionRoot() => new(
        ["--solution-root", "--SolutionRoot"],
        "Directory to search for the .sqlproj file. Defaults to the parent of --migrations-path.");

    internal static Option<int?> MinSqlVersion() => new(
        ["--min-sql-version", "--MinSqlVersion"],
        "Minimum SQL Server major version to enforce (e.g. 13 for 2016, 15 for 2019). " +
        "Defaults to reading from the .sqlproj DSP when --sql-project is set. " +
        "Set to 0 to skip the check entirely.");

    internal static Option<string> ConfigPath() => new(
        ["--config-path", "--ConfigPath"],
        description: "Path to config.json containing the TrackedTables array.",
        getDefaultValue: () => Path.Combine(Directory.GetCurrentDirectory(), "config.json"));

    internal static Option<string?> OutputMigrationDir() => new(
        ["--output-migration-dir", "--OutputMigrationDir"],
        "Root path for the generated migration script. Defaults to --migrations-path.");

    internal static Option<string?> ScriptName() => new(
        ["--script-name", "--ScriptName"],
        "Name for the generated migration script (no extension, e.g. 001_US123456_Description). " +
        "Prompts interactively if omitted.");

    internal static Option<string?> Action()
    {
        var opt = new Option<string?>(
            ["--action", "--Action"],
            "Action after change detection: Revert | Migrate | Cancel. Prompts interactively if omitted.");

        opt.AddValidator(r =>
        {
            var v = r.GetValueOrDefault<string?>();
            if (v is not null && v is not "Revert" and not "Migrate" and not "Cancel")
                r.ErrorMessage = "--action must be Revert, Migrate, or Cancel.";
        });

        return opt;
    }
}
