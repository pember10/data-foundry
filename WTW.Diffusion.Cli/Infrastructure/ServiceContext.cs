using WTW.Diffusion.Cli.Adapters;
using WTW.Diffusion.Core.Services;
using WTW.Diffusion.Core.Services.Database;
using WTW.Diffusion.Core.Services.Migration;

namespace WTW.Diffusion.Cli.Infrastructure;

/// <summary>
/// Resolves and wires all Core services from the CLI parameters.
/// Mirrors the parameter model of SqlMetadataAutomation.ps1: server and database
/// are supplied directly rather than as a connection string.
/// </summary>
internal sealed class ServiceContext
{
    public SqlMigrationRepository Repository   { get; }
    public MigrationScriptManager ScriptManager { get; }
    public ChangeDetectionService ChangeDetection { get; }
    public MigrationScriptGenerator ScriptGenerator { get; }
    public ShadowDatabaseManager ShadowManager { get; }

    public string TargetDatabase { get; }
    public string ShadowDatabase { get; }
    public string MigrationsPath { get; }

    /// <summary>
    /// Project manager for adding generated scripts to the .sqlproj.
    /// Null when --solution-root / --sql-project were not supplied.
    /// </summary>
    public FileSystemProjectManager? ProjectManager { get; }

    private ServiceContext(
        SqlMigrationRepository repository,
        MigrationScriptManager scriptManager,
        ChangeDetectionService changeDetection,
        MigrationScriptGenerator scriptGenerator,
        ShadowDatabaseManager shadowManager,
        string targetDatabase,
        string shadowDatabase,
        string migrationsPath,
        FileSystemProjectManager? projectManager = null)
    {
        Repository      = repository;
        ScriptManager   = scriptManager;
        ChangeDetection = changeDetection;
        ScriptGenerator = scriptGenerator;
        ShadowManager   = shadowManager;
        TargetDatabase  = targetDatabase;
        ShadowDatabase  = shadowDatabase;
        MigrationsPath  = migrationsPath;
        ProjectManager  = projectManager;
    }

    /// <summary>
    /// Builds a <see cref="ServiceContext"/> from individual connection parameters,
    /// matching the parameter model of SqlMetadataAutomation.ps1.
    /// </summary>
    /// <param name="targetServer">SQL Server instance name or hostname.</param>
    /// <param name="targetDatabase">Name of the target database.</param>
    /// <param name="migrationsPath">Absolute path to the migrations folder.</param>
    /// <param name="shadowDatabase">Shadow database name. Defaults to <c>{targetDatabase}_Shadow</c>.</param>
    /// <param name="migrationLogSchemaPath">
    /// Path to MigrationLogTableDefinition.sql. Defaults to the file beside the executable.
    /// </param>
    /// <param name="accessToken">Optional Azure SQL access token.</param>
    /// <param name="solutionRoot">
    /// Root directory to search for .sqlproj files.
    /// When supplied, <see cref="ProjectManager"/> is populated so callers can add generated scripts to the project.
    /// </param>
    public static ServiceContext Build(
        string targetServer,
        string targetDatabase,
        string migrationsPath,
        string? shadowDatabase         = null,
        string? migrationLogSchemaPath = null,
        string? accessToken            = null,
        string? solutionRoot           = null)
    {
        if (!Directory.Exists(migrationsPath))
            throw new DirectoryNotFoundException($"MigrationsPath not found: {migrationsPath}");

        shadowDatabase ??= $"{targetDatabase}_Shadow";

        migrationLogSchemaPath ??= Path.Combine(
            AppContext.BaseDirectory, "MigrationLogTableDefinition.sql");

        if (!File.Exists(migrationLogSchemaPath))
            throw new FileNotFoundException(
                $"MigrationLogTableDefinition.sql not found: {migrationLogSchemaPath}\n" +
                "Place the file alongside the executable or use --migration-log-schema.",
                migrationLogSchemaPath);

        string shadowCachePath = Path.Combine(AppContext.BaseDirectory, "shadow-cache.json");

        var repository      = new SqlMigrationRepository(targetServer, accessToken);
        var scriptManager   = new MigrationScriptManager(repository, migrationsPath);
        var changeDetection = new ChangeDetectionService(repository);
        var scriptGenerator = new MigrationScriptGenerator(repository);
        var shadowManager   = new ShadowDatabaseManager(
            repository, scriptManager, shadowDatabase,
            migrationLogSchemaPath, migrationsPath, shadowCachePath);

        FileSystemProjectManager? projectManager = solutionRoot is not null
            ? new FileSystemProjectManager(solutionRoot)
            : null;

        return new ServiceContext(
            repository, scriptManager, changeDetection, scriptGenerator, shadowManager,
            targetDatabase, shadowDatabase, migrationsPath, projectManager);
    }
}
