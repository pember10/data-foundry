using data_foundry.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace data_foundry.Services
{
    /// <summary>
    /// Manages migration script files and execution.
    /// </summary>
    public class MigrationScriptManager
    {
        private readonly SqlMigrationRepository _repository;
        private readonly string _migrationsPath;

        public MigrationScriptManager(SqlMigrationRepository repository, string migrationsPath)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _migrationsPath = migrationsPath ?? throw new ArgumentNullException(nameof(migrationsPath));

            if (!Directory.Exists(migrationsPath))
            {
                throw new DirectoryNotFoundException($"Migrations path not found: {migrationsPath}");
            }
        }

        /// <summary>
        /// Gets migration information from a script file.
        /// </summary>
        public MigrationInfo GetMigrationInfoFromFile(string path)
        {
            if (!File.Exists(path))
                return null;

            var content = File.ReadAllText(path);
            var match = Regex.Match(content, Constants.RegularExpressions.MigrationIdPattern);

            if (!match.Success)
                return null;

            return new MigrationInfo
            {
                Id = Guid.Parse(match.Groups["id"].Value),
                Content = content,
                FileName = Path.GetFileName(path),
                FullPath = path
            };
        }

        /// <summary>
        /// Gets all pending migrations for a database.
        /// </summary>
        public List<MigrationInfo> GetPendingMigrations(string database)
        {
            var executedIds = _repository.GetExecutedMigrationIds(database);
            var files = Directory.GetFiles(_migrationsPath, "*.sql", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToList();

            var pending = new List<MigrationInfo>();

            foreach (var file in files)
            {
                var info = GetMigrationInfoFromFile(file);
                if (info != null && !executedIds.Contains(info.Id))
                {
                    pending.Add(info);
                }
            }

            return pending;
        }

        /// <summary>
        /// Calculates SHA256 checksum for a file.
        /// </summary>
        public static string GetFileChecksum(string path)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(path))
                {
                    var hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
        }

        /// <summary>
        /// Gets a migration filename relative to the migrations root.
        /// </summary>
        public string GetRelativeFilename(string fullPath)
        {
            try
            {
                var rootResolved = Path.GetFullPath(_migrationsPath);
                var fullResolved = Path.GetFullPath(fullPath);
                var rootLeaf = Path.GetFileName(rootResolved);

                if (fullResolved.StartsWith(rootResolved, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = fullResolved.Substring(rootResolved.Length).TrimStart('\\', '/');
                    if (string.IsNullOrEmpty(relative))
                    {
                        return $"{rootLeaf}\\{Path.GetFileName(fullResolved)}";
                    }
                    return $"{rootLeaf}\\{relative}";
                }
            }
            catch
            {
                // Fall through to default
            }

            return Path.GetFileName(fullPath);
        }

        /// <summary>
        /// Executes a migration script and logs it.
        /// </summary>
        public void ExecuteMigrationScript(string database, MigrationInfo migrationInfo, bool skipExecution = false)
        {
            var displayName = GetRelativeFilename(migrationInfo.FullPath);

            if (!skipExecution)
            {
                _repository.ExecuteSqlScript(database, migrationInfo.Content);
            }

            var checksum = GetFileChecksum(migrationInfo.FullPath);
            _repository.LogMigrationExecution(database, migrationInfo.Id, displayName, checksum);
        }
    }
}
