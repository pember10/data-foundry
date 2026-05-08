using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Database;
using CoreConstants = WTW.Diffusion.Core.Constants;

namespace WTW.Diffusion.Core.Services.Migration
{
    /// <summary>
    /// Manages migration script files and execution.
    /// </summary>
    public class MigrationScriptManager : IMigrationScriptManager
    {
        private readonly ISqlMigrationRepository _repository;
        private readonly string _migrationsPath;

        public MigrationScriptManager(ISqlMigrationRepository repository, string migrationsPath)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _migrationsPath = migrationsPath ?? throw new ArgumentNullException(nameof(migrationsPath));

            if (!Directory.Exists(migrationsPath))
                throw new DirectoryNotFoundException($"Migrations path not found: {migrationsPath}");
        }

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

        public List<MigrationInfo> GetPendingMigrations(string database)
        {
            var executedIds = _repository.GetExecutedMigrationIds(database);
            var files = Directory.GetFiles(_migrationsPath, $"*.{CoreConstants.FileExtensions.Sql}", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToList();

            var pending = new List<MigrationInfo>();
            foreach (var file in files)
            {
                var info = GetMigrationInfoFromFile(file);
                if (info != null && !executedIds.Contains(info.Id))
                    pending.Add(info);
            }
            return pending;
        }

        public static string GetFileChecksum(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }

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
                    return string.IsNullOrEmpty(relative)
                        ? $"{rootLeaf}\\{Path.GetFileName(fullResolved)}"
                        : $"{rootLeaf}\\{relative}";
                }
            }
            catch (Exception)
            {
                // Exception is intentionally ignored because failure to resolve a relative path
                // is not critical; fallback to file name below.
            }

            return Path.GetFileName(fullPath);
        }

        public void ExecuteMigrationScript(string database, MigrationInfo migrationInfo, bool skipExecution = false)
        {
            var displayName = GetRelativeFilename(migrationInfo.FullPath);
            var checksum = GetFileChecksum(migrationInfo.FullPath);

            if (skipExecution)
            {
                _repository.LogMigrationExecution(database, migrationInfo.Id, displayName, checksum);
            }
            else
            {
                _repository.ExecuteScriptAndLog(database, migrationInfo.Content, migrationInfo.Id, displayName, checksum);
            }
        }
    }
}
