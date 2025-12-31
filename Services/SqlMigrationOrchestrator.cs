using data_foundry.Models;
using data_foundry.Options;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using EnvDTE;
using static data_foundry.Constants;

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

        private readonly SqlMigrationRepository _repository;
        private readonly MigrationScriptManager _scriptManager;
        private readonly ChangeDetectionService _changeDetection;
        private readonly MigrationScriptGenerator _scriptGenerator;

        /// <summary>
        /// Creates a new instance using DataFoundryOptions from the VS package.
        /// </summary>
        public SqlMigrationOrchestrator(DataFoundryOptions options, DTE environment)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            // Parse connection string to get database and server
            var (Database, Server) = ServicesHelper.ParseConnectionString(options.LocalDatabaseConnection);
            _targetDatabase = Database;
            _targetServer = Server;

            // Parse shadow database connection if provided
            if (!string.IsNullOrWhiteSpace(options.ShadowDatabaseConnection))
            {
                var (shadowDb, _) = ServicesHelper.ParseConnectionString(options.ShadowDatabaseConnection);
                _shadowDatabase = shadowDb;
            }
            else
            {
                _shadowDatabase = $"{_targetDatabase}_Shadow";
            }

            // Get the SQL project path from DTE
            var sqlProjectPath = GetSqlProjectPath(environment, options.SqlProject);
            if (string.IsNullOrEmpty(sqlProjectPath))
            {
                throw new InvalidOperationException($"SQL Project '{options.SqlProject}' not found in solution.");
            }

            var sqlProjectDir = Path.GetDirectoryName(sqlProjectPath);
            var migrationsPath = Path.Combine(sqlProjectDir, options.MigrationsFolder ?? Folders.Migrations);
            _outputMigrationDir = migrationsPath;

            // Config path - assume it's in Config folder at project root
            // NOTE: This is intentionally not using `options` to make it difficult to change the config
            _configPath = Path.Combine(sqlProjectDir, Folders.Config, "tablelist.json");
            _migrationLogSchemaPath = Path.Combine(sqlProjectDir, Folders.Config, "MigrationLogTableDefinition.sql");

            if (!Directory.Exists(migrationsPath))
                throw new DirectoryNotFoundException($"Migrations path not found: {migrationsPath}");

            if (!File.Exists(_configPath))
                throw new FileNotFoundException($"Config path not found: {_configPath}");

            // Initialize services
            var authProvider = new AzureSqlAuthenticationProvider();
            var accessToken = authProvider.GetAccessToken(_targetServer);

            _repository = new SqlMigrationRepository(_targetServer, accessToken);
            _scriptManager = new MigrationScriptManager(_repository, migrationsPath);
            _changeDetection = new ChangeDetectionService(_repository);
            _scriptGenerator = new MigrationScriptGenerator(_repository, _scriptManager);
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
            if (migrationsPath == null) throw new ArgumentNullException(nameof(migrationsPath));
            _configPath = configPath ?? Path.Combine(Path.GetDirectoryName(migrationsPath), Folders.Config, "tablelist.json");
            _outputMigrationDir = outputMigrationDir ?? migrationsPath;
            _migrationLogSchemaPath = migrationLogSchemaPath ?? Path.Combine(Path.GetDirectoryName(migrationsPath), Folders.Config, "MigrationLogTableDefinition.sql");

            if (!Directory.Exists(migrationsPath))
                throw new DirectoryNotFoundException($"MigrationsPath not found: {migrationsPath}");

            if (!File.Exists(_configPath))
                throw new FileNotFoundException($"ConfigPath not found: {_configPath}");

            // Initialize services
            var authProvider = new AzureSqlAuthenticationProvider();
            var accessToken = authProvider.GetAccessToken(_targetServer);

            _repository = new SqlMigrationRepository(_targetServer, accessToken);
            _scriptManager = new MigrationScriptManager(_repository, migrationsPath);
            _changeDetection = new ChangeDetectionService(_repository);
            _scriptGenerator = new MigrationScriptGenerator(_repository, _scriptManager);
        }

        private static string GetSqlProjectPath(DTE environment, string projectName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (environment?.Solution?.Projects == null)
                return null;

            foreach (Project project in environment.Solution.Projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName))
                    continue;

                if (!project.FullName.ToLowerInvariant().EndsWith(".sqlproj"))
                    continue;

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
        public void ExecuteTargetMigrations(bool requireConfirmation = false, Action<string> logger = null)
        {
            logger = logger ?? Console.WriteLine;

            // Ensure database exists (skip for Azure SQL)
            if (!_targetServer.Contains(Constants.Azure.AzureSqlDomain))
            {
                logger("Ensuring target database");
                _repository.CreateDatabaseIfMissing(_targetDatabase);
            }

            logger("Ensuring migration log table");
            _repository.EnsureMigrationLogTable(_targetDatabase, _migrationLogSchemaPath);

            var pendingMigrations = _scriptManager.GetPendingMigrations(_targetDatabase);

            if (pendingMigrations.Count == 0)
            {
                logger("No pending migrations found.");
                return;
            }

            if (requireConfirmation)
            {
                logger("Pending migrations:");
                foreach (var m in pendingMigrations)
                {
                    logger($"  -> {m.FileName} [{m.Id}]");
                }
                
                logger("Execute these migrations? (y/n)");
                // Note: In a real UI implementation, you'd get user input here
                // For now, this is just a placeholder
            }

            foreach (var migration in pendingMigrations)
            {
                logger($"Executing migration {migration.Id} ({migration.FileName}) on {_targetDatabase}");
                _scriptManager.ExecuteMigrationScript(_targetDatabase, migration);
            }

            logger("Migrations complete.");
        }

        /// <summary>
        /// Detects and handles data changes between target and shadow databases.
        /// </summary>
        public List<TableChangeSummary> DetectAndHandleChanges(MigrationAction? action = null, Action<string> logger = null)
        {
            logger = logger ?? Console.WriteLine;

            var config = LoadConfig();
            if (config.TrackedTables == null || config.TrackedTables.Count == 0)
            {
                logger("No TrackedTables found in config.");
                return new List<TableChangeSummary>();
            }

            // Create shadow database and apply migrations
            logger("Recreating shadow database");
            _repository.DropAndRecreateDatabase(_shadowDatabase);
            _repository.EnsureMigrationLogTable(_shadowDatabase, _migrationLogSchemaPath);

            var pendingShadow = _scriptManager.GetPendingMigrations(_shadowDatabase);
            foreach (var migration in pendingShadow)
            {
                _scriptManager.ExecuteMigrationScript(_shadowDatabase, migration);
            }
            logger("Shadow migrations complete.");

            // Detect changes
            logger("Detecting changes between target and shadow...");
            var summary = _changeDetection.GetChangesSummary(_targetDatabase, _shadowDatabase, config.TrackedTables);
            var diffs = summary.Where(s => s.HasChanges).ToList();

            if (diffs.Count == 0)
            {
                logger("No changes detected.");
                return summary;
            }

            logger("Changes detected:");
            foreach (var diff in diffs)
            {
                logger($"  {diff.Table}: {diff.Inserts} inserts, {diff.Updates} updates, {diff.Deletes} deletes");
            }

            if (!action.HasValue)
            {
                // In a real UI, you'd prompt the user here
                logger("No action specified. Skipping change handling.");
                return summary;
            }

            var tables = diffs.Select(d => d.Table).ToList();

            switch (action.Value)
            {
                case MigrationAction.Revert:
                    logger($"Reverting changes in {_targetDatabase}");
                    _changeDetection.RevertChanges(_targetDatabase, _shadowDatabase, tables);
                    logger("Revert complete.");
                    break;

                case MigrationAction.Migrate:
                    logger("Generating migration script");
                    // In a real UI, you'd prompt for script name
                    var scriptName = $"Migration_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var filePath = _scriptGenerator.GenerateMigrationScript(
                        _targetDatabase, _shadowDatabase, tables, _outputMigrationDir, scriptName);
                    logger($"Generated migration script: {filePath}");

                    // Log the generated script
                    var migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                    if (migrationInfo != null)
                    {
                        logger("Logging newly generated migration script to migration log");
                        _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);
                    }
                    break;

                case MigrationAction.Cancel:
                    logger("Action cancelled. No changes applied.");
                    break;
            }

            return summary;
        }

        /// <summary>
        /// Executes the full migration workflow.
        /// </summary>
        public void Execute(bool confirmTargetMigration = false, bool detectChanges = false, MigrationAction? action = null, Action<string> logger = null)
        {
            logger = logger ?? Console.WriteLine;

            try
            {
                // Execute target migrations
                ExecuteTargetMigrations(confirmTargetMigration, logger);

                // Detect and handle changes if requested
                if (detectChanges)
                {
                    DetectAndHandleChanges(action, logger);
                }
            }
            finally
            {
                // Clear connection pools
                SqlConnection.ClearAllPools();
            }
        }

        private MigrationConfig LoadConfig()
        {
            var json = File.ReadAllText(_configPath);
            return JsonConvert.DeserializeObject<MigrationConfig>(json);
        }
    }
}
