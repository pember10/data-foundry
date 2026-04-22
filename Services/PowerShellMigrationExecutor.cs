using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using data_foundry.Options;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using CoreConstants = WTW.Diffusion.Core.Constants;

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
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));

            var scriptPath = GetPowerShellScriptPath();
            _psRunner = new PowerShellScriptRunner(scriptPath);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation, Action<string> logger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ThreadHelper.JoinableTaskFactory.Run(() => ExecuteTargetMigrationsAsync(requireConfirmation, logger));
        }

        public async Task ExecuteTargetMigrationsAsync(bool requireConfirmation, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { CoreConstants.Parameters.TargetDatabase, database },
                { CoreConstants.Parameters.TargetServer, server },
                { CoreConstants.Parameters.MigrationsPath, GetMigrationsPath() },
                { CoreConstants.Parameters.ConfirmTargetMigration, requireConfirmation },
                { CoreConstants.Parameters.DetectChanges, false }
            };

            var result = await _psRunner.ExecuteAsync(parameters, logger).ConfigureAwait(true);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"PowerShell script failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }
        }

        public List<TableChangeSummary> DetectAndHandleChanges(string action, Action<string> logger)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ThreadHelper.JoinableTaskFactory.Run(() => DetectAndHandleChangesAsync(action, logger));
        }

        public async Task<List<TableChangeSummary>> DetectAndHandleChangesAsync(string action, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { CoreConstants.Parameters.TargetDatabase, database },
                { CoreConstants.Parameters.TargetServer, server },
                { CoreConstants.Parameters.MigrationsPath, GetMigrationsPath() },
                { CoreConstants.Parameters.DetectChanges, true },
                { CoreConstants.Parameters.ConfigPath, GetTableListConfigPath() }
            };

            if (!string.IsNullOrEmpty(action))
            {
                parameters[CoreConstants.Parameters.Action] = action;
            }

            var result = await _psRunner.ExecuteAsync(parameters, logger).ConfigureAwait(true);

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
            ThreadHelper.ThrowIfNotOnUIThread();
            return ThreadHelper.JoinableTaskFactory.Run(() => GetPendingMigrationsForTargetAsync());
        }

        public async Task<List<MigrationInfo>> GetPendingMigrationsForTargetAsync()
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            // Run script without DetectChanges to just check pending migrations
            var parameters = new Dictionary<string, object>
            {
                { CoreConstants.Parameters.TargetDatabase, database },
                { CoreConstants.Parameters.TargetServer, server },
                { CoreConstants.Parameters.MigrationsPath, GetMigrationsPath() }
            };

            var result = await _psRunner.ExecuteAsync(parameters, null).ConfigureAwait(true);

            if (!result.Success)
            {
                // Return empty list on error to avoid breaking sync check
                return new List<MigrationInfo>();
            }

            return PowerShellOutputParser.ParsePendingMigrations(result.Output);
        }

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ThreadHelper.JoinableTaskFactory.Run(() => GenerateMigrationScriptWithNameAsync(tableNames, scriptName));
        }

        public async Task<string> GenerateMigrationScriptWithNameAsync(List<string> tableNames, string scriptName)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { CoreConstants.Parameters.TargetDatabase, database },
                { CoreConstants.Parameters.TargetServer, server },
                { CoreConstants.Parameters.MigrationsPath, GetMigrationsPath() },
                { CoreConstants.Parameters.DetectChanges, true },
                { CoreConstants.Parameters.Action, CoreConstants.Actions.Migrate },
                { CoreConstants.Parameters.ScriptName, scriptName },  // Pre-supply script name!
                { CoreConstants.Parameters.ConfigPath, GetTableListConfigPath() }
            };

            var result = await _psRunner.ExecuteAsync(parameters, null).ConfigureAwait(true);

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
            ThreadHelper.ThrowIfNotOnUIThread();
            ThreadHelper.JoinableTaskFactory.Run(() => RevertChangesAsync(tableNames, logger));
        }

        public async Task RevertChangesAsync(List<string> tableNames, Action<string> logger)
        {
            var (database, server) = ServicesHelper.ParseConnectionString(_options.LocalDatabaseConnection);

            var parameters = new Dictionary<string, object>
            {
                { CoreConstants.Parameters.TargetDatabase, database },
                { CoreConstants.Parameters.TargetServer, server },
                { CoreConstants.Parameters.MigrationsPath, GetMigrationsPath() },
                { CoreConstants.Parameters.DetectChanges, true },
                { CoreConstants.Parameters.Action, CoreConstants.Actions.Revert },  // Revert action
                { CoreConstants.Parameters.ConfigPath, GetTableListConfigPath() }
            };

            var result = await _psRunner.ExecuteAsync(parameters, logger).ConfigureAwait(true);

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"Revert failed: {string.Join(Environment.NewLine, result.Errors)}"
                );
            }
        }

        private static string GetPowerShellScriptPath()
        {
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(PowerShellMigrationExecutor));
            return Path.Combine(installDir, CoreConstants.Folders.Scripts, CoreConstants.StaticFiles.SqlMetadataAutomationPs1);
        }

        private string GetMigrationsPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_environment?.Solution?.Projects == null)
                throw new InvalidOperationException("No solution loaded");

            foreach (Project project in _environment.Solution.Projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName))
                    continue;

                if (!project.FullName.ToLowerInvariant().EndsWith(CoreConstants.FileExtensions.SqlProj))
                    continue;

                if (string.Equals(project.Name, _options.SqlProject, StringComparison.OrdinalIgnoreCase))
                {
                    var projectDir = Path.GetDirectoryName(project.FullName);
                    return Path.Combine(projectDir, _options.MigrationsFolder ?? CoreConstants.Folders.Migrations);
                }
            }

            throw new InvalidOperationException($"SQL Project '{_options.SqlProject}' not found in solution.");
        }

        private static string GetTableListConfigPath()
        {
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(PowerShellMigrationExecutor));
            return Path.Combine(installDir, CoreConstants.Folders.Config, CoreConstants.StaticFiles.TableListJson);
        }
    }
}
