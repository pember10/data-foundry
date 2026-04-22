using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Database;
using WTW.Diffusion.Core.Services.Migration;

namespace WTW.Diffusion.Core.Services
{
    /// <summary>
    /// Manages the lifecycle of the shadow database used for change detection.
    /// Handles creation, migration execution, hash-based caching, and validation.
    /// </summary>
    public class ShadowDatabaseManager(
        SqlMigrationRepository repository,
        MigrationScriptManager scriptManager,
        string shadowDatabase,
        string migrationLogSchemaPath,
        string migrationsDir,
        string shadowCacheFilePath,
        ILogger logger = null)
    {
        private readonly SqlMigrationRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        private readonly MigrationScriptManager _scriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
        private readonly string _shadowDatabase = shadowDatabase ?? throw new ArgumentNullException(nameof(shadowDatabase));
        private readonly string _migrationLogSchemaPath = migrationLogSchemaPath ?? throw new ArgumentNullException(nameof(migrationLogSchemaPath));
        private readonly string _migrationsDir = migrationsDir ?? throw new ArgumentNullException(nameof(migrationsDir));
        private static string _lastShadowMigrationHash;
        private static readonly object _shadowDbLock = new();

        /// <summary>
        /// Ensures the shadow database exists and is up to date with all migration scripts.
        /// Uses a two-level cache (in-memory + disk) to avoid unnecessary recreation.
        /// </summary>
        /// <returns>True if the shadow database was recreated; false if the cached version was used.</returns>
        public bool EnsureUpToDate()
        {
            string currentHash = GetMigrationsHash();
            bool needsRecreate = false;
            string cacheSource = "memory";

            lock (_shadowDbLock)
            {
                if (!_repository.DatabaseExists(_shadowDatabase))
                {
                    needsRecreate = true;
                    logger?.Log("Shadow database does not exist");
                }
                else if (_lastShadowMigrationHash != null && _lastShadowMigrationHash == currentHash)
                {
                    needsRecreate = false;
                }
                else
                {
                    ShadowDatabaseCacheInfo cachedInfo = LoadCache();
                    if (cachedInfo != null && cachedInfo.Hash == currentHash && ValidateCache(cachedInfo))
                    {
                        cacheSource = "disk";
                        needsRecreate = false;
                        _lastShadowMigrationHash = currentHash;
                    }
                    else
                    {
                        needsRecreate = true;
                        if (cachedInfo != null)
                            logger?.Log("Shadow database cache invalid — migrations have changed");
                    }
                }
            }

            if (needsRecreate)
            {
                Recreate();

                lock (_shadowDbLock)
                {
                    _lastShadowMigrationHash = currentHash;
                    var migrations = _scriptManager.GetPendingMigrations(_shadowDatabase);
                    SaveCache(currentHash, migrations.Count);
                }
            }
            else
            {
                logger?.Log($"Shadow database is up to date (source: {cacheSource})");
            }

            return needsRecreate;
        }

        /// <summary>
        /// Forces the shadow database to be dropped and fully rebuilt from all migration scripts.
        /// </summary>
        public void Recreate()
        {
            logger?.Log("Step 1/4: Dropping existing shadow database...");
            _repository.DropAndRecreateDatabase(_shadowDatabase);

            logger?.Log("Step 2/4: Creating migration log table...");
            _repository.EnsureMigrationLogTable(_shadowDatabase, _migrationLogSchemaPath);

            logger?.Log("Step 3/4: Loading pending migrations...");
            List<MigrationInfo> pending = _scriptManager.GetPendingMigrations(_shadowDatabase);
            logger?.Log($"Found {pending.Count} migration(s) to apply");

            logger?.Log("Step 4/4: Executing migrations...");
            for (int i = 0; i < pending.Count; i++)
            {
                MigrationInfo migration = pending[i];
                logger?.Log($"  [{i + 1}/{pending.Count}] Applying {Path.GetFileName(migration.FileName)}...");
                _scriptManager.ExecuteMigrationScript(_shadowDatabase, migration);
            }
        }

        /// <summary>
        /// Invalidates the in-memory cache, forcing the next call to EnsureUpToDate to re-evaluate.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_shadowDbLock)
                _lastShadowMigrationHash = null;
        }

        /// <summary>
        /// Computes a hash of all migration file names and last-write timestamps.
        /// </summary>
        public string GetMigrationsHash()
        {
            try
            {
                List<string> files = [.. Directory.GetFiles(_migrationsDir, "*.sql", SearchOption.AllDirectories).OrderBy(f => f)];

                if (files.Count == 0)
                    return "EMPTY";

                string combined = string.Join("|", files.Select(f =>
                    $"{Path.GetFileName(f)}:{new FileInfo(f).LastWriteTimeUtc.Ticks}"));

                using var sha256 = SHA256.Create();
                byte[] bytes = Encoding.UTF8.GetBytes(combined);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "");
            }
            catch
            {
                return Guid.NewGuid().ToString(); // Force refresh on error
            }
        }

        private ShadowDatabaseCacheInfo LoadCache()
        {
            if (string.IsNullOrEmpty(shadowCacheFilePath) || !File.Exists(shadowCacheFilePath))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<ShadowDatabaseCacheInfo>(
                    File.ReadAllText(shadowCacheFilePath));
            }
            catch
            {
                return null;
            }
        }

        private void SaveCache(string hash, int migrationCount)
        {
            if (string.IsNullOrEmpty(shadowCacheFilePath))
                return;

            try
            {
                var info = new ShadowDatabaseCacheInfo
                {
                    Hash = hash,
                    MigrationCount = migrationCount,
                    LastUpdated = DateTime.UtcNow,
                    DatabaseName = _shadowDatabase
                };

                File.WriteAllText(shadowCacheFilePath,
                    JsonConvert.SerializeObject(info, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save shadow cache: {ex.Message}");
            }
        }

        private bool ValidateCache(ShadowDatabaseCacheInfo cache)
        {
            if (cache == null || cache.DatabaseName != _shadowDatabase)
                return false;

            try
            {
                List<Guid> applied = _repository.GetExecutedMigrationIds(_shadowDatabase);
                return applied.Count == cache.MigrationCount;
            }
            catch
            {
                return false;
            }
        }
    }
}
