using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using data_foundry.Options;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Config;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services;
using WTW.Diffusion.Core.Services.Database;
using WTW.Diffusion.Core.Services.Migration;
using CoreConstants = WTW.Diffusion.Core.Constants;

namespace data_foundry.Services
{
    /// <summary>
    /// Orchestrates SQL Server database migration execution, shadow database creation, and data change detection.
    /// </summary>
    public class SqlMigrationOrchestrator
    {
        private readonly string _targetDatabase;
        private readonly string _targetServer;
        private readonly string _shadowDatabase;
        private readonly string _configPath;
        private readonly string _outputMigrationDir;
        private readonly string _migrationLogSchemaPath;

        private SqlMigrationRepository _repository;
        private MigrationScriptManager _scriptManager;
        private ChangeDetectionService _changeDetection;
        private MigrationScriptGenerator _scriptGenerator;
        private ShadowDatabaseManager _shadowManager;
        private readonly ProjectIntegrationService _projectIntegration;

        // PowerShell executor (if enabled)
        private readonly IMigrationExecutor _powerShellExecutor;

        /// <summary>
        /// Creates a new instance using DataFoundryOptions from the VS package.
        /// </summary>
        public SqlMigrationOrchestrator(DataFoundryOptions options, DTE environment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            // Check if PowerShell mode is enabled - if so, create PS executor and skip C# init
            if (options.UsePowerShellScript)
            {
                _powerShellExecutor = new PowerShellMigrationExecutor(options, environment);

                // Set minimal fields for backwards compatibility
                (string db, string srv) = ServicesHelper.ParseConnectionString(options.LocalDatabaseConnection);
                _targetDatabase = db;
                _targetServer = srv;
                _shadowDatabase = !string.IsNullOrWhiteSpace(options.ShadowDatabaseConnection)
                    ? ServicesHelper.ParseConnectionString(options.ShadowDatabaseConnection).Database
                    : $"{_targetDatabase}_Shadow";

                return; // Skip rest of initialization
            }

            // Original C# mode initialization...

            // Parse connection string to get database and server
            (string Database, string Server) = ServicesHelper.ParseConnectionString(options.LocalDatabaseConnection);
            _targetDatabase = Database;
            _targetServer = Server;

            // Parse shadow database connection if provided
            if (!string.IsNullOrWhiteSpace(options.ShadowDatabaseConnection))
            {
                (string shadowDb, string _) = ServicesHelper.ParseConnectionString(options.ShadowDatabaseConnection);
                _shadowDatabase = shadowDb;
            }
            else
            {
                _shadowDatabase = $"{_targetDatabase}_Shadow";
            }

            // Get the SQL project path from DTE
            string sqlProjectPath = GetSqlProjectPath(environment, options.SqlProject);
            if (string.IsNullOrEmpty(sqlProjectPath))
            {
                throw new InvalidOperationException($"SQL Project '{options.SqlProject}' not found in solution.");
            }

            string sqlProjectDir = PathHelper.GetSafeDirectoryName(sqlProjectPath);
            if (string.IsNullOrEmpty(sqlProjectDir))
            {
                throw new InvalidOperationException($"Invalid SQL Project path: {sqlProjectPath}");
            }

            string migrationsPath = PathHelper.SafeCombine(sqlProjectDir, options.MigrationsFolder ?? CoreConstants.Folders.Migrations);
            if (string.IsNullOrEmpty(migrationsPath))
            {
                throw new InvalidOperationException($"Invalid migrations path combination: {sqlProjectDir} + {options.MigrationsFolder}");
            }

            _outputMigrationDir = migrationsPath;

            // Config path - use extension installation directory with fallback
            string installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
            string configDir = PathHelper.SafeCombine(installDir, CoreConstants.Folders.Config);

            if (string.IsNullOrEmpty(configDir) || !PathHelper.EnsureDirectoryExists(configDir))
            {
                throw new InvalidOperationException("Failed to create or access config directory.");
            }

            _configPath = PathHelper.SafeCombine(configDir, "tablelist.json");
            _migrationLogSchemaPath = PathHelper.SafeCombine(configDir, "MigrationLogTableDefinition.sql");

            if (string.IsNullOrEmpty(_configPath) || string.IsNullOrEmpty(_migrationLogSchemaPath))
            {
                throw new InvalidOperationException("Failed to construct config file paths.");
            }

            if (!Directory.Exists(migrationsPath))
            {
                throw new DirectoryNotFoundException($"Migrations path not found: {migrationsPath}");
            }

            // Auto-create config file if it doesn't exist
            EnsureConfigFileExists(_configPath);

            if (!File.Exists(_migrationLogSchemaPath))
            {
                throw new FileNotFoundException($"Migration log schema not found: {_migrationLogSchemaPath}");
            }

            // Set shadow cache file path
            string cacheDir = PathHelper.GetSafeDirectoryName(_configPath);
            string shadowCacheFilePath = !string.IsNullOrEmpty(cacheDir)
                ? PathHelper.SafeCombine(cacheDir, "shadow-cache.json")
                : null;

            // Initialize services
            AzureSqlAuthenticationProvider authProvider = new AzureSqlAuthenticationProvider();
            string accessToken = authProvider.GetAccessToken(_targetServer);

            InitializeServices(accessToken, migrationsPath, shadowCacheFilePath);
            _projectIntegration = new ProjectIntegrationService(
                new ProjectFileManager(environment), options.SqlProject);
        }

        public SqlMigrationOrchestrator(
            string targetDatabase,
            string targetServer,
            string migrationsPath,
            string configPath = null,
            string outputMigrationDir = null,
            string migrationLogSchemaPath = null)
        {
            _targetDatabase = targetDatabase ?? throw new ArgumentNullException(nameof(targetDatabase));
            _targetServer = targetServer ?? throw new ArgumentNullException(nameof(targetServer));
            _shadowDatabase = $"{_targetDatabase}_Shadow";
            if (migrationsPath == null)
            {
                throw new ArgumentNullException(nameof(migrationsPath));
            }

            // If no config path specified, use extension installation directory with fallback
            if (string.IsNullOrEmpty(configPath))
            {
                string installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
                string configDir = PathHelper.SafeCombine(installDir, CoreConstants.Folders.Config);

                if (!string.IsNullOrEmpty(configDir))
                {
                    _ = PathHelper.EnsureDirectoryExists(configDir);
                    _configPath = PathHelper.SafeCombine(configDir, "tablelist.json");
                }

                if (string.IsNullOrEmpty(_configPath))
                {
                    throw new InvalidOperationException("Failed to determine config path.");
                }
            }
            else
            {
                if (!PathHelper.IsValidPath(configPath))
                {
                    throw new ArgumentException($"Invalid config path: {configPath}", nameof(configPath));
                }
                _configPath = configPath;
            }

            _outputMigrationDir = outputMigrationDir ?? migrationsPath;

            // If no migration log schema path specified, use extension installation directory with fallback
            if (string.IsNullOrEmpty(migrationLogSchemaPath))
            {
                string installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
                string configDir = PathHelper.SafeCombine(installDir, CoreConstants.Folders.Config);

                if (!string.IsNullOrEmpty(configDir))
                {
                    _ = PathHelper.EnsureDirectoryExists(configDir);
                    _migrationLogSchemaPath = PathHelper.SafeCombine(configDir, "MigrationLogTableDefinition.sql");
                }

                if (string.IsNullOrEmpty(_migrationLogSchemaPath))
                {
                    throw new InvalidOperationException("Failed to determine migration log schema path.");
                }
            }
            else
            {
                if (!PathHelper.IsValidPath(migrationLogSchemaPath))
                {
                    throw new ArgumentException($"Invalid migration log schema path: {migrationLogSchemaPath}", nameof(migrationLogSchemaPath));
                }
                _migrationLogSchemaPath = migrationLogSchemaPath;
            }

            if (!Directory.Exists(migrationsPath))
            {
                throw new DirectoryNotFoundException($"MigrationsPath not found: {migrationsPath}");
            }

            // Auto-create config file if it doesn't exist
            EnsureConfigFileExists(_configPath);

            if (!File.Exists(_migrationLogSchemaPath))
            {
                throw new FileNotFoundException($"Migration log schema not found: {_migrationLogSchemaPath}");
            }

            // Set shadow cache file path
            string cacheDir = PathHelper.GetSafeDirectoryName(_configPath);
            string shadowCacheFilePath = !string.IsNullOrEmpty(cacheDir)
                ? PathHelper.SafeCombine(cacheDir, "shadow-cache.json")
                : null;

            // Initialize services
            AzureSqlAuthenticationProvider authProvider = new AzureSqlAuthenticationProvider();
            string accessToken = authProvider.GetAccessToken(_targetServer);

            InitializeServices(accessToken, migrationsPath, shadowCacheFilePath);
        }

        private void InitializeServices(string accessToken, string migrationsPath, string shadowCacheFilePath)
        {
            _repository = new SqlMigrationRepository(_targetServer, accessToken);
            _scriptManager = new MigrationScriptManager(_repository, migrationsPath);
            _changeDetection = new ChangeDetectionService(_repository);
            _scriptGenerator = new MigrationScriptGenerator(_repository);
            _shadowManager = new ShadowDatabaseManager(
                _repository, _scriptManager, _shadowDatabase,
                _migrationLogSchemaPath, migrationsPath, shadowCacheFilePath);
        }

        private static string GetSqlProjectPath(DTE environment, string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (environment?.Solution?.Projects == null)
            {
                return null;
            }

            foreach (Project project in environment.Solution.Projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName))
                {
                    continue;
                }

                if (!project.FullName.ToLowerInvariant().EndsWith(".sqlproj"))
                {
                    continue;
                }

                if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    return project.FullName;
                }
            }

            return null;
        }

        /// <summary>
        /// Executes pending migrations on the target database.
        /// </summary>
        public void ExecuteTargetMigrations(bool requireConfirmation = false, ILogger logger = null)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                _powerShellExecutor.ExecuteTargetMigrations(requireConfirmation, logger);
                return;
            }

            // Original C# implementation
            logger = logger ?? ConsoleLogger.Instance;
            Stopwatch stopwatch = Stopwatch.StartNew();
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.Deployment,
                    "Deployment started");

                // Ensure database exists (skip for Azure SQL)
                if (!_targetServer.Contains(CoreConstants.Azure.AzureSqlDomain))
                {
                    logger.Log("Ensuring target database");
                    _repository.CreateDatabaseIfMissing(_targetDatabase);
                }

                logger.Log("Ensuring migration log table");
                _repository.EnsureMigrationLogTable(_targetDatabase, _migrationLogSchemaPath);

                List<MigrationInfo> pendingMigrations = _scriptManager.GetPendingMigrations(_targetDatabase);

                if (pendingMigrations.Count == 0)
                {
                    logger.Log("No pending migrations found.");

                    // Update activity
                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            "No pending migrations",
                            stopwatch.Elapsed);
                    }
                    return;
                }

                if (requireConfirmation)
                {
                    logger.Log("Pending migrations:");
                    foreach (MigrationInfo m in pendingMigrations)
                    {
                        logger.Log($"  -> {m.FileName} [{m.Id}]");
                    }

                    logger.Log("Execute these migrations? (y/n)");
                    // Note: In a real UI implementation, you'd get user input here
                    // For now, this is just a placeholder
                }

                foreach (MigrationInfo migration in pendingMigrations)
                {
                    logger.Log($"Executing migration {migration.Id} ({migration.FileName}) on {_targetDatabase}");
                    _scriptManager.ExecuteMigrationScript(_targetDatabase, migration);
                }

                logger.Log("Migrations complete.");

                // Update activity with success
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Success,
                        $"{pendingMigrations.Count} migration(s) executed successfully",
                        stopwatch.Elapsed);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);

                // Update activity with failure
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Failed,
                        $"Deployment failed: {ex.Message}",
                        stopwatch.Elapsed);
                }

                throw;
            }
        }

        /// <summary>
        /// Detects and handles data changes between target and shadow databases.
        /// </summary>
        /// <remarks>TODO: This desperately needs to be refactored, it's too massive!</remarks>
        public List<TableChangeSummary> DetectAndHandleChanges(MigrationAction? action = null, ILogger logger = null)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                string actionString = action.HasValue ? action.Value.ToString() : null;
                return _powerShellExecutor.DetectAndHandleChanges(actionString, logger);
            }

            // Original C# implementation
            logger = logger ?? ConsoleLogger.Instance;
            Stopwatch stopwatch = Stopwatch.StartNew();
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.ChangeDetection,
                    "Change detection started");

                TableListConfig config = LoadConfig();
                if (config.Tables == null || config.Tables.Count == 0)
                {
                    logger.Log("No TrackedTables found in config.");

                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Warning,
                            "No tracked tables configured",
                            stopwatch.Elapsed);
                    }
                    return new List<TableChangeSummary>();
                }

                // Delegate shadow lifecycle to ShadowDatabaseManager
                logger.Log("Synchronizing shadow database...");
                _ = _shadowManager.EnsureUpToDate();
                logger.Log($"Shadow database ready ({stopwatch.Elapsed.TotalSeconds:F1}s)");

                // Detect changes
                logger.Log("Detecting changes between target and shadow...");
                Stopwatch detectStopwatch = Stopwatch.StartNew();

                logger.Log($"Analyzing {config.Tables.Count} tables...");
                List<TableChangeSummary> summary = _changeDetection.GetChangesSummary(_targetDatabase, _shadowDatabase, config.Tables);

                double detectTime = detectStopwatch.Elapsed.TotalSeconds;
                logger.Log($"Change detection completed in {detectTime:F1}s ({detectTime / config.Tables.Count:F2}s per table)");

                List<TableChangeSummary> diffs = summary.Where(s => s.HasChanges).ToList();

                if (diffs.Count == 0)
                {
                    logger.Log("No changes detected.");

                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            "No changes detected",
                            stopwatch.Elapsed);
                    }
                    return summary;
                }

                logger.Log("Changes detected:");
                foreach (TableChangeSummary diff in diffs)
                {
                    logger.Log($"  {diff.Table}: {diff.Inserts} inserts, {diff.Updates} updates, {diff.Deletes} deletes");
                }

                if (!action.HasValue)
                {
                    // In a real UI, you'd prompt the user here
                    logger.Log("No action specified. Skipping change handling.");

                    if (activity != null)
                    {
                        int totalChanges = diffs.Sum(d => d.Inserts + d.Updates + d.Deletes);
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            $"{diffs.Count} table(s) with changes detected ({totalChanges} total changes)",
                            stopwatch.Elapsed);
                    }
                    return summary;
                }

                List<string> tables = diffs.Select(d => d.Table).ToList();

                switch (action.Value)
                {
                    case MigrationAction.Revert:
                        logger.Log($"Reverting changes in {_targetDatabase}");
                        _changeDetection.RevertChanges(_targetDatabase, _shadowDatabase, tables);
                        logger.Log("Revert complete.");

                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Success,
                                $"Changes reverted for {tables.Count} table(s)",
                                stopwatch.Elapsed);
                        }
                        break;

                    case MigrationAction.Migrate:
                        logger.Log("Generating migration script");
                        // In a real UI, you'd prompt for script name
                        string scriptName = $"Migration_{DateTime.Now:yyyyMMdd_HHmmss}";
                        string filePath = _scriptGenerator.GenerateMigrationScript(
                            _targetDatabase, _shadowDatabase, tables, _outputMigrationDir, scriptName);
                        logger.Log($"Generated migration script: {filePath}");

                        // Log the generated script
                        MigrationInfo migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                        if (migrationInfo != null)
                        {
                            logger.Log("Logging newly generated migration script to migration log");
                            _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);
                        }

                        _projectIntegration?.AddScriptToProject(filePath);

                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Success,
                                $"Migration script generated: {Path.GetFileName(filePath)}",
                                stopwatch.Elapsed);
                        }
                        break;

                    case MigrationAction.Cancel:
                        logger.Log("Action cancelled. No changes applied.");

                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Cancelled,
                                "Operation cancelled by user",
                                stopwatch.Elapsed);
                        }
                        break;
                }

                return summary;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);

                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Failed,
                        $"Change detection failed: {ex.Message}",
                        stopwatch.Elapsed);
                }

                throw;
            }
        }

        /// <summary>
        /// Executes the full migration workflow.
        /// </summary>
        public void Execute(bool confirmTargetMigration = false, bool detectChanges = false, MigrationAction? action = null, ILogger logger = null)
        {
            logger = logger ?? ConsoleLogger.Instance;

            try
            {
                // Execute target migrations
                ExecuteTargetMigrations(confirmTargetMigration, logger);

                // Detect and handle changes if requested
                if (detectChanges)
                {
                    _ = DetectAndHandleChanges(action, logger);
                }
            }
            finally
            {
                // Clear connection pools
                SqlConnection.ClearAllPools();
            }
        }

        /// <summary>
        /// Generates a migration script with a custom name for the specified tables.
        /// </summary>
        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                return _powerShellExecutor.GenerateMigrationScriptWithName(tableNames, scriptName);
            }

            // Original C# implementation
            Stopwatch stopwatch = Stopwatch.StartNew();
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.MigrationGeneration,
                    "Migration script generation started");

                // Generate the migration script using existing shadow database
                string filePath = _scriptGenerator.GenerateMigrationScript(
                    _targetDatabase,
                    _shadowDatabase,
                    tableNames,
                    _outputMigrationDir,
                    scriptName);

                // Log the generated script to migration log (skip execution)
                MigrationInfo migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                if (migrationInfo != null)
                {
                    _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);
                }

                _projectIntegration?.AddScriptToProject(filePath);

                // Update activity with success
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Success,
                        $"Migration script generated: {Path.GetFileName(filePath)}",
                        stopwatch.Elapsed);
                }

                return filePath;
            }
            catch (Exception ex)
            {
                // Update activity with failure
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Failed,
                        $"Script generation failed: {ex.Message}",
                        stopwatch.Elapsed);
                }

                throw;
            }
            finally
            {
                // Clear connection pools
                SqlConnection.ClearAllPools();
            }
        }

        private TableListConfig LoadConfig()
        {
            string json = File.ReadAllText(_configPath);
            return JsonConvert.DeserializeObject<TableListConfig>(json);
        }

        /// <summary>
        /// Gets the list of pending migrations that have not been applied to the target database.
        /// </summary>
        /// <returns>List of pending migrations, or empty list if all are applied</returns>
        public List<MigrationInfo> GetPendingMigrationsForTarget()
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                return _powerShellExecutor.GetPendingMigrationsForTarget();
            }

            // Original C# implementation
            return _scriptManager.GetPendingMigrations(_targetDatabase);
        }

        /// <summary>
        /// Checks if the target database is in sync with all migration scripts.
        /// </summary>
        /// <returns>True if all migrations are applied, false if there are pending migrations</returns>
        public bool IsTargetDatabaseInSync()
        {
            List<MigrationInfo> pending = GetPendingMigrationsForTarget();
            return pending == null || pending.Count == 0;
        }

        /// <summary>
        /// Reverts changes in target database to match shadow database.
        /// </summary>
        public void RevertChanges(List<string> tableNames, ILogger logger = null)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                _powerShellExecutor.RevertChanges(tableNames, logger);
                return;
            }

            // C# implementation - call DetectAndHandleChanges with Revert action
            _ = DetectAndHandleChanges(MigrationAction.Revert, logger);
        }

        private static void EnsureConfigFileExists(string configPath)
        {
            if (File.Exists(configPath))
            {
                return;
            }

            // Create default config with empty tracked tables
            TableListConfig defaultConfig = new TableListConfig
            {
                Tables = new List<string>()
            };

            string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);

            // Ensure directory exists
            string directory = PathHelper.GetSafeDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = PathHelper.EnsureDirectoryExists(directory);
                File.WriteAllText(configPath, json);
            }
        }
    }
}
