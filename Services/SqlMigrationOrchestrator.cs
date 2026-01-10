using data_foundry.Models;
using data_foundry.Options;
using data_foundry.Helpers;
using data_foundry.Config;
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

        // PowerShell executor (if enabled)
        private readonly IMigrationExecutor _powerShellExecutor;

        // Performance optimization: Track shadow database state (in-memory + persisted)
        private static string _lastShadowMigrationHash = null;
        private static readonly object _shadowDbLock = new object();
        private readonly string _shadowCacheFilePath;

        /// <summary>
        /// Creates a new instance using DataFoundryOptions from the VS package.
        /// </summary>
        public SqlMigrationOrchestrator(DataFoundryOptions options, DTE environment)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            // Check if PowerShell mode is enabled - if so, create PS executor and skip C# init
            if (options.UsePowerShellScript)
            {
                _powerShellExecutor = new PowerShellMigrationExecutor(options, environment);
                
                // Set minimal fields for backwards compatibility
                var (db, srv) = ServicesHelper.ParseConnectionString(options.LocalDatabaseConnection);
                _targetDatabase = db;
                _targetServer = srv;
                _shadowDatabase = !string.IsNullOrWhiteSpace(options.ShadowDatabaseConnection)
                    ? ServicesHelper.ParseConnectionString(options.ShadowDatabaseConnection).Database
                    : $"{_targetDatabase}_Shadow";
                
                return; // Skip rest of initialization
            }

            // Original C# mode initialization...
            
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

            var sqlProjectDir = PathHelper.GetSafeDirectoryName(sqlProjectPath);
            if (string.IsNullOrEmpty(sqlProjectDir))
            {
                throw new InvalidOperationException($"Invalid SQL Project path: {sqlProjectPath}");
            }

            var migrationsPath = PathHelper.SafeCombine(sqlProjectDir, options.MigrationsFolder ?? Folders.Migrations);
            if (string.IsNullOrEmpty(migrationsPath))
            {
                throw new InvalidOperationException($"Invalid migrations path combination: {sqlProjectDir} + {options.MigrationsFolder}");
            }

            _outputMigrationDir = migrationsPath;

            // Config path - use extension installation directory with fallback
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
            var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
            
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
                throw new DirectoryNotFoundException($"Migrations path not found: {migrationsPath}");

            // Auto-create config file if it doesn't exist
            EnsureConfigFileExists(_configPath);

            if (!File.Exists(_migrationLogSchemaPath))
                throw new FileNotFoundException($"Migration log schema not found: {_migrationLogSchemaPath}");

            // Set shadow cache file path
            var cacheDir = PathHelper.GetSafeDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(cacheDir))
            {
                _shadowCacheFilePath = PathHelper.SafeCombine(cacheDir, "shadow-cache.json");
            }

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
            
            // If no config path specified, use extension installation directory with fallback
            if (string.IsNullOrEmpty(configPath))
            {
                var installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
                var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
                
                if (!string.IsNullOrEmpty(configDir))
                {
                    PathHelper.EnsureDirectoryExists(configDir);
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
                var installDir = PathHelper.GetExtensionInstallDirectory(typeof(SqlMigrationOrchestrator));
                var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
                
                if (!string.IsNullOrEmpty(configDir))
                {
                    PathHelper.EnsureDirectoryExists(configDir);
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
                throw new DirectoryNotFoundException($"MigrationsPath not found: {migrationsPath}");

            // Auto-create config file if it doesn't exist
            EnsureConfigFileExists(_configPath);

            if (!File.Exists(_migrationLogSchemaPath))
                throw new FileNotFoundException($"Migration log schema not found: {_migrationLogSchemaPath}");

            // Set shadow cache file path
            var cacheDir = PathHelper.GetSafeDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(cacheDir))
            {
                _shadowCacheFilePath = PathHelper.SafeCombine(cacheDir, "shadow-cache.json");
            }

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
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                _powerShellExecutor.ExecuteTargetMigrations(requireConfirmation, logger);
                return;
            }

            // Original C# implementation
            logger = logger ?? Console.WriteLine;
            var startTime = DateTime.Now;
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.Deployment,
                    "Deployment started");

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
                    
                    // Update activity
                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            "No pending migrations",
                            DateTime.Now - startTime);
                    }
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

                // Update activity with success
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Success,
                        $"{pendingMigrations.Count} migration(s) executed successfully",
                        DateTime.Now - startTime);
                }
            }
            catch (Exception ex)
            {
                logger($"ERROR: {ex.Message}");

                // Update activity with failure
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Failed,
                        $"Deployment failed: {ex.Message}",
                        DateTime.Now - startTime);
                }

                throw;
            }
        }

        /// <summary>
        /// Detects and handles data changes between target and shadow databases.
        /// </summary>
        public List<TableChangeSummary> DetectAndHandleChanges(MigrationAction? action = null, Action<string> logger = null)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                string actionString = action.HasValue ? action.Value.ToString() : null;
                return _powerShellExecutor.DetectAndHandleChanges(actionString, logger);
            }

            // Original C# implementation
            logger = logger ?? Console.WriteLine;
            var startTime = DateTime.Now;
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.ChangeDetection,
                    "Change detection started");

                var config = LoadConfig();
                if (config.Tables == null || config.Tables.Count == 0)
                {
                    logger("No TrackedTables found in config.");
                    
                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Warning,
                            "No tracked tables configured",
                            DateTime.Now - startTime);
                    }
                    return new List<TableChangeSummary>();
                }

                // PERFORMANCE OPTIMIZATION: Smart shadow database management with persistent cache
                var currentMigrationHash = GetMigrationsHash();
                bool needsRecreate = false;
                string cacheSource = "memory";
                
                lock (_shadowDbLock)
                {
                    // First check: Does shadow database exist?
                    if (!_repository.DatabaseExists(_shadowDatabase))
                    {
                        needsRecreate = true;
                        logger("Shadow database does not exist");
                    }
                    else
                    {
                        // Second check: In-memory cache
                        if (_lastShadowMigrationHash != null && _lastShadowMigrationHash == currentMigrationHash)
                        {
                            cacheSource = "memory";
                            needsRecreate = false;
                        }
                        else
                        {
                            // Third check: Persisted cache
                            var cachedInfo = LoadShadowCache();
                            if (cachedInfo != null && cachedInfo.Hash == currentMigrationHash)
                            {
                                // Cache hit - validate integrity
                                if (ValidateShadowDatabase(cachedInfo))
                                {
                                    cacheSource = "disk";
                                    needsRecreate = false;
                                    // Update in-memory cache
                                    _lastShadowMigrationHash = currentMigrationHash;
                                }
                                else
                                {
                                    logger("Shadow database validation failed - cache corrupted");
                                    needsRecreate = true;
                                }
                            }
                            else
                            {
                                // Cache miss - migrations changed
                                needsRecreate = true;
                            }
                        }
                    }
                }

                if (needsRecreate)
                {
                    logger("Shadow database out of date or missing - recreating...");
                    var syncStart = DateTime.Now;
                    
                    logger("Step 1/4: Dropping existing shadow database...");
                    _repository.DropAndRecreateDatabase(_shadowDatabase);
                    
                    logger("Step 2/4: Creating migration log table...");
                    _repository.EnsureMigrationLogTable(_shadowDatabase, _migrationLogSchemaPath);

                    logger("Step 3/4: Loading pending migrations...");
                    var pendingShadow = _scriptManager.GetPendingMigrations(_shadowDatabase);
                    logger($"Found {pendingShadow.Count} migration(s) to apply");
                    
                    logger("Step 4/4: Executing migrations...");
                    for (int i = 0; i < pendingShadow.Count; i++)
                    {
                        var migration = pendingShadow[i];
                        var migrationName = Path.GetFileName(migration.FileName);
                        logger($"  [{i + 1}/{pendingShadow.Count}] Applying {migrationName}...");
                        _scriptManager.ExecuteMigrationScript(_shadowDatabase, migration);
                    }
                    
                    lock (_shadowDbLock)
                    {
                        _lastShadowMigrationHash = currentMigrationHash;
                        SaveShadowCache(currentMigrationHash, pendingShadow.Count);
                    }
                    
                    logger($"Shadow database synchronized in {(DateTime.Now - syncStart).TotalSeconds:F1}s");
                }
                else
                {
                    logger($"Shadow database is up to date - using cached version (source: {cacheSource})");
                }

                // Detect changes
                logger("Detecting changes between target and shadow...");
                var detectStart = DateTime.Now;
                
                logger($"Analyzing {config.Tables.Count} tables...");
                var summary = _changeDetection.GetChangesSummary(_targetDatabase, _shadowDatabase, config.Tables);
                
                var detectTime = (DateTime.Now - detectStart).TotalSeconds;
                logger($"Change detection completed in {detectTime:F1}s ({detectTime / config.Tables.Count:F2}s per table)");
                
                var diffs = summary.Where(s => s.HasChanges).ToList();

                if (diffs.Count == 0)
                {
                    logger("No changes detected.");
                    
                    if (activity != null)
                    {
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            "No changes detected",
                            DateTime.Now - startTime);
                    }
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
                    
                    if (activity != null)
                    {
                        var totalChanges = diffs.Sum(d => d.Inserts + d.Updates + d.Deletes);
                        ActivityHistoryService.Instance.UpdateActivity(
                            activity,
                            ActivityStatus.Success,
                            $"{diffs.Count} table(s) with changes detected ({totalChanges} total changes)",
                            DateTime.Now - startTime);
                    }
                    return summary;
                }

                var tables = diffs.Select(d => d.Table).ToList();

                switch (action.Value)
                {
                    case MigrationAction.Revert:
                        logger($"Reverting changes in {_targetDatabase}");
                        _changeDetection.RevertChanges(_targetDatabase, _shadowDatabase, tables);
                        logger("Revert complete.");
                        
                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Success,
                                $"Changes reverted for {tables.Count} table(s)",
                                DateTime.Now - startTime);
                        }
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
                        
                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Success,
                                $"Migration script generated: {Path.GetFileName(filePath)}",
                                DateTime.Now - startTime);
                        }
                        break;

                    case MigrationAction.Cancel:
                        logger("Action cancelled. No changes applied.");
                        
                        if (activity != null)
                        {
                            ActivityHistoryService.Instance.UpdateActivity(
                                activity,
                                ActivityStatus.Cancelled,
                                "Operation cancelled by user",
                                DateTime.Now - startTime);
                        }
                        break;
                }

                return summary;
            }
            catch (Exception ex)
            {
                logger($"ERROR: {ex.Message}");

                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Failed,
                        $"Change detection failed: {ex.Message}",
                        DateTime.Now - startTime);
                }

                throw;
            }
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
            var startTime = DateTime.Now;
            ActivityEntry activity = null;

            try
            {
                // Start tracking activity
                activity = ActivityHistoryService.Instance.StartActivity(
                    ActivityType.MigrationGeneration,
                    "Migration script generation started");

                // Create shadow database and apply migrations
                _repository.DropAndRecreateDatabase(_shadowDatabase);
                _repository.EnsureMigrationLogTable(_shadowDatabase, _migrationLogSchemaPath);

                var pendingShadow = _scriptManager.GetPendingMigrations(_shadowDatabase);
                foreach (var migration in pendingShadow)
                {
                    _scriptManager.ExecuteMigrationScript(_shadowDatabase, migration);
                }

                // Generate the migration script
                var filePath = _scriptGenerator.GenerateMigrationScript(
                    _targetDatabase, 
                    _shadowDatabase, 
                    tableNames, 
                    _outputMigrationDir, 
                    scriptName);

                // Log the generated script to migration log (skip execution)
                var migrationInfo = _scriptManager.GetMigrationInfoFromFile(filePath);
                if (migrationInfo != null)
                {
                    _scriptManager.ExecuteMigrationScript(_targetDatabase, migrationInfo, skipExecution: true);
                }

                // Update activity with success
                if (activity != null)
                {
                    ActivityHistoryService.Instance.UpdateActivity(
                        activity,
                        ActivityStatus.Success,
                        $"Migration script generated: {Path.GetFileName(filePath)}",
                        DateTime.Now - startTime);
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
                        DateTime.Now - startTime);
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
            var json = File.ReadAllText(_configPath);
            return JsonConvert.DeserializeObject<TableListConfig>(json);
        }

        /// <summary>
        /// Calculates a hash of all migration files to detect if shadow database needs refresh.
        /// </summary>
        private string GetMigrationsHash()
        {
            try
            {
                var files = Directory.GetFiles(_outputMigrationDir, "*.sql", SearchOption.AllDirectories)
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count == 0)
                    return "EMPTY";

                var combinedHash = string.Join("|", files.Select(f => 
                    $"{Path.GetFileName(f)}:{new FileInfo(f).LastWriteTimeUtc.Ticks}"));

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(combinedHash);
                    var hash = sha256.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
            catch
            {
                return Guid.NewGuid().ToString(); // Force refresh on error
            }
        }

        /// <summary>
        /// Loads the cached shadow database information from disk.
        /// </summary>
        private ShadowDatabaseCacheInfo LoadShadowCache()
        {
            if (string.IsNullOrEmpty(_shadowCacheFilePath) || !File.Exists(_shadowCacheFilePath))
                return null;

            try
            {
                var json = File.ReadAllText(_shadowCacheFilePath);
                return JsonConvert.DeserializeObject<ShadowDatabaseCacheInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves the shadow database cache information to disk.
        /// </summary>
        private void SaveShadowCache(string hash, int migrationCount)
        {
            if (string.IsNullOrEmpty(_shadowCacheFilePath))
                return;

            try
            {
                var cacheInfo = new ShadowDatabaseCacheInfo
                {
                    Hash = hash,
                    MigrationCount = migrationCount,
                    LastUpdated = DateTime.UtcNow,
                    DatabaseName = _shadowDatabase
                };

                var json = JsonConvert.SerializeObject(cacheInfo, Formatting.Indented);
                File.WriteAllText(_shadowCacheFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save shadow cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that the shadow database matches the cached state.
        /// </summary>
        private bool ValidateShadowDatabase(ShadowDatabaseCacheInfo cache)
        {
            if (cache == null || cache.DatabaseName != _shadowDatabase)
                return false;

            try
            {
                // Quick validation: check migration count
                var appliedMigrations = _repository.GetExecutedMigrationIds(_shadowDatabase);
                return appliedMigrations.Count == cache.MigrationCount;
            }
            catch
            {
                return false;
            }
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
            var pending = GetPendingMigrationsForTarget();
            return pending == null || pending.Count == 0;
        }

        /// <summary>
        /// Reverts changes in target database to match shadow database.
        /// </summary>
        public void RevertChanges(List<string> tableNames, Action<string> logger = null)
        {
            // Delegate to PowerShell if enabled
            if (_powerShellExecutor != null)
            {
                _powerShellExecutor.RevertChanges(tableNames, logger);
                return;
            }

            // C# implementation - call DetectAndHandleChanges with Revert action
            DetectAndHandleChanges(MigrationAction.Revert, logger);
        }

        private static void EnsureConfigFileExists(string configPath)
        {
            if (File.Exists(configPath))
                return;

            // Create default config with empty tracked tables
            var defaultConfig = new TableListConfig
            {
                Tables = new List<string>()
            };

            var json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
            
            // Ensure directory exists
            var directory = PathHelper.GetSafeDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                PathHelper.EnsureDirectoryExists(directory);
                File.WriteAllText(configPath, json);
            }
        }
    }
}
