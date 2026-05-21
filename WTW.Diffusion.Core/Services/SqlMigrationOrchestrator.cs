using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Config;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Database;
using WTW.Diffusion.Core.Services.Migration;

namespace WTW.Diffusion.Core.Services
{
    /// <summary>
    /// Orchestrates SQL Server database migration execution, shadow database creation, and data change detection.
    /// Platform-independent: all host-specific concerns are supplied via ILogger, IConfigurationProvider,
    /// and IProjectManager.
    /// </summary>
    public class SqlMigrationOrchestrator
    {
        private readonly ILogger _logger;
        private readonly IConfigurationProvider _configuration;
        private readonly IProjectManager _projectManager;

        private readonly string _targetDatabase;
        private readonly string _targetServer;
        private readonly string _shadowDatabase;
        private readonly string _configPath;
        private readonly string _outputMigrationDir;
        private readonly string _migrationLogSchemaPath;

        private readonly SqlMigrationRepository _repository;
        private readonly MigrationScriptManager _scriptManager;
        private readonly ChangeDetectionService _changeDetection;
        private readonly MigrationScriptGenerator _scriptGenerator;
        private readonly ShadowDatabaseManager _shadowManager;

        /// <summary>
        /// Creates a new instance using the three Core abstractions.
        /// </summary>
        /// <param name="logger">Required. Receives all log output.</param>
        /// <param name="configuration">Required. Supplies connection strings, project name, migrations folder, and tracked tables.</param>
        /// <param name="projectManager">Required. Handles adding generated scripts to the SQL project.</param>
        /// <param name="migrationLogSchemaPath">
        /// Path to MigrationLogTableDefinition.sql.
        /// Defaults to Config/MigrationLogTableDefinition.sql beside the Core assembly.
        /// </param>
        /// <param name="shadowCachePath">
        /// Path where shadow-cache.json is persisted.
        /// Defaults to Config/shadow-cache.json beside the Core assembly.
        /// </param>
        public SqlMigrationOrchestrator(
            ILogger logger,
            IConfigurationProvider configuration,
            IProjectManager projectManager,
            string migrationLogSchemaPath = null,
            string shadowCachePath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _projectManager = projectManager ?? throw new ArgumentNullException(nameof(projectManager));

            // Parse target connection string
            (_targetDatabase, _targetServer) = ParseConnectionString(configuration.LocalDatabaseConnection);

            // Derive shadow database name
            if (!string.IsNullOrWhiteSpace(configuration.ShadowDatabaseConnection))
            {
                (_shadowDatabase, _) = ParseConnectionString(configuration.ShadowDatabaseConnection);
            }
            else
            {
                _shadowDatabase = _targetDatabase + "_Shadow";
            }

            // Resolve migrations path via IProjectManager
            string sqlProjectPath = _projectManager.GetProjectPath(configuration.SqlProject);
            if (string.IsNullOrEmpty(sqlProjectPath))
                throw new InvalidOperationException(
                    "SQL Project '" + configuration.SqlProject + "' not found in solution.");

            string sqlProjectDir = PathHelper.GetSafeDirectoryName(sqlProjectPath);
            if (string.IsNullOrEmpty(sqlProjectDir))
                throw new InvalidOperationException("Invalid SQL Project path: " + sqlProjectPath);

            string migrationsFolder = string.IsNullOrEmpty(configuration.MigrationsFolder)
                ? Constants.Folders.Migrations
                : configuration.MigrationsFolder;

            string migrationsPath = PathHelper.SafeCombine(sqlProjectDir, migrationsFolder);
            if (string.IsNullOrEmpty(migrationsPath))
                throw new InvalidOperationException(
                    "Invalid migrations path: " + sqlProjectDir + " + " + migrationsFolder);

            if (!Directory.Exists(migrationsPath))
                throw new DirectoryNotFoundException("Migrations path not found: " + migrationsPath);

            _outputMigrationDir = migrationsPath;

            // Resolve config and schema paths — fall back to the directory beside the Core assembly
            string defaultConfigDir = ResolveDefaultConfigDir();

            _configPath = BuildConfigPath(defaultConfigDir);

            _migrationLogSchemaPath = !string.IsNullOrEmpty(migrationLogSchemaPath)
                ? migrationLogSchemaPath
                : PathHelper.SafeCombine(defaultConfigDir, "MigrationLogTableDefinition.sql");

            string resolvedShadowCachePath = !string.IsNullOrEmpty(shadowCachePath)
                ? shadowCachePath
                : PathHelper.SafeCombine(defaultConfigDir, "shadow-cache.json");

            if (string.IsNullOrEmpty(_migrationLogSchemaPath))
                throw new InvalidOperationException("Failed to determine migration log schema path.");

            if (!File.Exists(_migrationLogSchemaPath))
                throw new FileNotFoundException(
                    "Migration log schema not found: " + _migrationLogSchemaPath,
                    _migrationLogSchemaPath);

            EnsureConfigFileExists(_configPath);

            // Acquire Azure token when targeting Azure SQL
            string accessToken = null;
            if (_targetServer != null &&
                _targetServer.IndexOf(Constants.Azure.AzureSqlDomain, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var authProvider = new AzureSqlAuthenticationProvider();
                accessToken = authProvider.GetAccessToken(_targetServer);
            }

            // Wire Core services
            _repository = new SqlMigrationRepository(_targetServer, accessToken);
            _scriptManager = new MigrationScriptManager(_repository, migrationsPath);
            _changeDetection = new ChangeDetectionService(_repository, _logger);
            _scriptGenerator = new MigrationScriptGenerator(_repository);
            _shadowManager = new ShadowDatabaseManager(
                _repository, _scriptManager, _shadowDatabase,
                _migrationLogSchemaPath, migrationsPath, resolvedShadowCachePath, _logger);
        }

        // ─── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Executes pending migrations on the target database.
        /// </summary>
        public void ExecuteTargetMigrations(bool requireConfirmation = false, CancellationToken ct = default)
        {
            try
            {
                ValidateServerVersion();

                if (!_targetServer.Contains(Constants.Azure.AzureSqlDomain))
                {
                    _logger.Log("Ensuring target database");
                    _repository.CreateDatabaseIfMissing(_targetDatabase);
                }

                _logger.Log("Ensuring migration log table");
                _repository.EnsureMigrationLogTable(_targetDatabase, _migrationLogSchemaPath);

                List<MigrationInfo> pendingMigrations = _scriptManager.GetPendingMigrations(_targetDatabase);

                if (pendingMigrations.Count == 0)
                {
                    _logger.Log("No pending migrations found.");
                    return;
                }

                if (requireConfirmation)
                {
                    _logger.Log("Pending migrations:");
                    foreach (MigrationInfo m in pendingMigrations)
                        _logger.Log("  -> " + m.FileName + " [" + m.Id + "]");
                }

                foreach (MigrationInfo migration in pendingMigrations)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger.Log("Executing migration " + migration.Id + " (" + migration.FileName + ") on " + _targetDatabase);
                    _scriptManager.ExecuteMigrationScript(_targetDatabase, migration);
                }

                _logger.Log("Migrations complete.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Migration execution cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Detects data changes between the target and shadow databases and optionally handles them.
        /// </summary>
        public List<TableChangeSummary> DetectAndHandleChanges(
            MigrationAction? action = null,
            CancellationToken ct = default)
        {
            try
            {
                List<string> trackedTables = GetTrackedTables();
                if (trackedTables.Count == 0)
                {
                    _logger.Log("No TrackedTables found in config.");
                    return new List<TableChangeSummary>();
                }

                _logger.Log("Synchronizing shadow database...");
                _shadowManager.EnsureUpToDate(ct);
                _logger.Log("Shadow database ready.");

                _logger.Log("Detecting changes between target and shadow...");
                _logger.Log("Analyzing " + trackedTables.Count + " tables...");
                List<TableChangeSummary> summary = _changeDetection.GetChangesSummary(
                    _targetDatabase, _shadowDatabase, trackedTables, ct);

                List<TableChangeSummary> diffs = summary.Where(s => s.HasChanges).ToList();

                if (diffs.Count == 0)
                {
                    _logger.Log("No changes detected.");
                    return summary;
                }

                _logger.Log("Changes detected:");
                foreach (TableChangeSummary diff in diffs)
                    _logger.Log("  " + diff.Table + ": " + diff.Inserts + " inserts, " + diff.Updates + " updates, " + diff.Deletes + " deletes");

                if (!action.HasValue)
                {
                    _logger.Log("No action specified. Skipping change handling.");
                    return summary;
                }

                List<string> tables = diffs.Select(d => d.Table).ToList();
                HandleAction(action.Value, tables, ct);
                return summary;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Change detection cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Reverts changes in the target database to match the shadow — without re-running change detection.
        /// </summary>
        public void RevertChanges(List<string> tableNames, CancellationToken ct = default)
        {
            if (tableNames == null || tableNames.Count == 0)
                throw new ArgumentException("tableNames must not be empty.", nameof(tableNames));

            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.Log("Reverting changes in " + _targetDatabase);
                _changeDetection.RevertChanges(_targetDatabase, _shadowDatabase, tableNames);
                _logger.Log("Revert complete.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Revert cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generates a migration script with a custom name for the specified tables.
        /// Logs the script to the migration log and adds it to the SQL project.
        /// </summary>
        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            try
            {
                string filePath = _scriptGenerator.GenerateMigrationScript(
                    _targetDatabase, _shadowDatabase, tableNames, _outputMigrationDir, scriptName);

                MigrationInfo migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                if (migrationInfo != null)
                    _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);

                AddScriptToProject(filePath);

                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Executes the full migration workflow: apply pending migrations then optionally detect/handle changes.
        /// </summary>
        public void Execute(
            bool confirmTargetMigration = false,
            bool detectChanges = false,
            MigrationAction? action = null,
            CancellationToken ct = default)
        {
            try
            {
                ExecuteTargetMigrations(confirmTargetMigration, ct);

                if (detectChanges)
                    DetectAndHandleChanges(action, ct);
            }
            finally
            {
                SqlConnection.ClearAllPools();
            }
        }

        /// <summary>
        /// Returns migrations that have not yet been applied to the target database.
        /// </summary>
        public List<MigrationInfo> GetPendingMigrationsForTarget()
        {
            return _scriptManager.GetPendingMigrations(_targetDatabase);
        }

        /// <summary>
        /// Returns true when all migration scripts have been applied to the target database.
        /// </summary>
        public bool IsTargetDatabaseInSync()
        {
            List<MigrationInfo> pending = GetPendingMigrationsForTarget();
            return pending == null || pending.Count == 0;
        }

        // ─── Private helpers ───────────────────────────────────────────────────

        private void ValidateServerVersion()
        {
            string sqlProject = _configuration.SqlProject;
            if (string.IsNullOrEmpty(sqlProject))
                return;

            string projectPath = _projectManager.GetProjectPath(sqlProject);
            SqlProjectDspReader.ValidateAndWarn(
                _targetDatabase,
                _repository,
                _logger,
                _configuration.MinSqlServerVersion,
                projectPath);
        }

        private void HandleAction(MigrationAction action, List<string> tables, CancellationToken ct)
        {
            switch (action)
            {
                case MigrationAction.Revert:
                    _logger.Log("Reverting changes in " + _targetDatabase);
                    _changeDetection.RevertChanges(_targetDatabase, _shadowDatabase, tables);
                    _logger.Log("Revert complete.");
                    break;

                case MigrationAction.Migrate:
                    _logger.Log("Generating migration script");
                    string scriptName = "Migration_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string filePath = _scriptGenerator.GenerateMigrationScript(
                        _targetDatabase, _shadowDatabase, tables, _outputMigrationDir, scriptName);
                    _logger.Log("Generated migration script: " + filePath);

                    MigrationInfo migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                    if (migrationInfo != null)
                    {
                        _logger.Log("Logging newly generated migration script to migration log");
                        _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);
                    }

                    AddScriptToProject(filePath);
                    break;

                case MigrationAction.Cancel:
                    _logger.Log("Action cancelled. No changes applied.");
                    break;
            }
        }

        private void AddScriptToProject(string filePath)
        {
            string sqlProject = _configuration.SqlProject;
            if (string.IsNullOrEmpty(sqlProject))
                return;

            string projectPath = _projectManager.GetProjectPath(sqlProject);
            if (string.IsNullOrEmpty(projectPath))
            {
                _logger.LogWarning("Could not find project '" + sqlProject + "' to add script.");
                return;
            }

            string folderPath = _projectManager.GetRelativeFolderPath(projectPath, filePath);
            bool added = _projectManager.AddFileToProject(sqlProject, filePath, folderPath);
            if (!added)
                _logger.LogWarning("Failed to add script to project: " + filePath);
        }

        private List<string> GetTrackedTables()
        {
            string[] fromConfig = _configuration.TrackedTables;
            if (fromConfig != null && fromConfig.Length > 0)
                return new List<string>(fromConfig);

            // Fallback: read tablelist.json from the config path
            if (!string.IsNullOrEmpty(_configPath) && File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                TableListConfig tableList = JsonConvert.DeserializeObject<TableListConfig>(json);
                if (tableList != null && tableList.Tables != null && tableList.Tables.Count > 0)
                    return tableList.Tables;
            }

            return new List<string>();
        }

        private static (string database, string server) ParseConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return (null, null);

            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return (builder.InitialCatalog, builder.DataSource);
            }
            catch
            {
                return (null, null);
            }
        }

        private static string ResolveDefaultConfigDir()
        {
            string installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
            string configDir = PathHelper.SafeCombine(installDir, Constants.Folders.Config);

            if (!string.IsNullOrEmpty(configDir))
                PathHelper.EnsureDirectoryExists(configDir);

            return configDir ?? installDir;
        }

        private static string BuildConfigPath(string configDir)
        {
            string configPath = PathHelper.SafeCombine(configDir, "tablelist.json");
            if (string.IsNullOrEmpty(configPath))
                throw new InvalidOperationException("Failed to construct config file path.");
            return configPath;
        }

        private static void EnsureConfigFileExists(string configPath)
        {
            if (File.Exists(configPath))
                return;

            TableListConfig defaultConfig = new TableListConfig
            {
                Tables = new List<string>()
            };

            string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            string directory = PathHelper.GetSafeDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                PathHelper.EnsureDirectoryExists(directory);
                File.WriteAllText(configPath, json);
            }
        }
    }
}
