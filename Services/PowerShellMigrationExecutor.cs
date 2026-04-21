using System;
using System.Collections.Generic;
using System.IO;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using data_foundry.Options;
using EnvDTE;

namespace data_foundry.Services
{
    /// <summary>
    /// Executes migrations using SqlMetadataAutomation.ps1 PowerShell script.
    /// </summary>
    public class PowerShellMigrationExecutor : IMigrationExecutor
    {
        private readonly DataFoundryOptions _options;
        private readonly DTE _environment;
        private readonly PowerShellScriptRunner _psRunner;

        public PowerShellMigrationExecutor(DataFoundryOptions options, DTE environment)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            _options = options;
            _environment = environment;

            var scriptPath = GetPowerShellScriptPath();
            _psRunner = new PowerShellScriptRunner(scriptPath);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { "TargetDatabase", database },
                { "TargetServer", server },
                { "MigrationsPath", GetMigrationsPath() },
                { "ConfirmTargetMigration", requireConfirmation },
                { "DetectChanges", false }
            };

            var result = _psRunner.ExecuteAsync(parameters, logger).Result;

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"PowerShell script failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }
        }

        public List<TableChangeSummary> DetectAndHandleChanges(string action, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { "TargetDatabase", database },
                { "TargetServer", server },
                { "MigrationsPath", GetMigrationsPath() },
                { "DetectChanges", true },
                { "ConfigPath", GetTableListConfigPath() }
            };

            if (!string.IsNullOrEmpty(action))
            {
                parameters["Action"] = action;
            }

            var result = _psRunner.ExecuteAsync(parameters, logger).Result;

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"PowerShell script failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }

            return PowerShellOutputParser.ParseChanges(result.Output);
        }

        public List<MigrationInfo> GetPendingMigrationsForTarget()
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            // Run script without DetectChanges to just check pending migrations
            var parameters = new Dictionary<string, object>
            {
                { "TargetDatabase", database },
                { "TargetServer", server },
                { "MigrationsPath", GetMigrationsPath() }
            };

            var result = _psRunner.ExecuteAsync(parameters, null).Result;

            if (!result.Success)
            {
                // Return empty list on error to avoid breaking sync check
                return new List<MigrationInfo>();
            }

            return PowerShellOutputParser.ParsePendingMigrations(result.Output);
        }

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { "TargetDatabase", database },
                { "TargetServer", server },
                { "MigrationsPath", GetMigrationsPath() },
                { "DetectChanges", true },
                { "Action", "Migrate" },
                { "ScriptName", scriptName },  // Pre-supply script name!
                { "ConfigPath", GetTableListConfigPath() }
            };

            var result = _psRunner.ExecuteAsync(parameters, null).Result;

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Script generation failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }

            return PowerShellOutputParser.ParseGeneratedScriptPath(result.Output);
        }

        public void RevertChanges(List<string> tableNames, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { "TargetDatabase", database },
                { "TargetServer", server },
                { "MigrationsPath", GetMigrationsPath() },
                { "DetectChanges", true },
                { "Action", "Revert" },  // Revert action
                { "ConfigPath", GetTableListConfigPath() }
            };

            var result = _psRunner.ExecuteAsync(parameters, logger).Result;

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Revert failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }
        }

        private string GetPowerShellScriptPath()
        {
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(PowerShellMigrationExecutor));
            return Path.Combine(installDir, "Scripts", "SqlMetadataAutomation.ps1");
        }

        private string GetMigrationsPath()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            if (_environment?.Solution?.Projects == null)
                throw new InvalidOperationException("No solution loaded");

            foreach (Project project in _environment.Solution.Projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName))
                    continue;

                if (!project.FullName.ToLowerInvariant().EndsWith(".sqlproj"))
                    continue;

                if (string.Equals(project.Name, _options.SqlProject, StringComparison.OrdinalIgnoreCase))
                {
                    var projectDir = Path.GetDirectoryName(project.FullName);
                    return Path.Combine(projectDir, _options.MigrationsFolder ?? "Migrations");
                }
            }

            throw new InvalidOperationException($"SQL Project '{_options.SqlProject}' not found in solution.");
        }

        private string GetTableListConfigPath()
        {
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(PowerShellMigrationExecutor));
            return Path.Combine(installDir, WTW.Diffusion.Core.Constants.Folders.Config, "tablelist.json");
        }
    }
}
