using System;
using System.Collections.Generic;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using data_foundry.Options;
using EnvDTE;

namespace data_foundry.Services
{
    /// <summary>
    /// Wrapper that delegates to either C# or PowerShell executor based on settings.
    /// Acts as a drop-in replacement for SqlMigrationOrchestrator when PowerShell mode is enabled.
    /// </summary>
    public class PowerShellOrchestratorWrapper
    {
        private readonly IMigrationExecutor _executor;

        public PowerShellOrchestratorWrapper(DataFoundryOptions options, DTE environment)
        {
            _executor = new PowerShellMigrationExecutor(options, environment);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation = false, Action<string> logger = null)
        {
            _executor.ExecuteTargetMigrations(requireConfirmation, logger);
        }

        public List<TableChangeSummary> DetectAndHandleChanges(MigrationAction? action = null, Action<string> logger = null)
        {
            string actionString = action.HasValue ? action.Value.ToString() : null;
            return _executor.DetectAndHandleChanges(actionString, logger);
        }

        public List<MigrationInfo> GetPendingMigrationsForTarget()
        {
            return _executor.GetPendingMigrationsForTarget();
        }

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            return _executor.GenerateMigrationScriptWithName(tableNames, scriptName);
        }

        // For backwards compatibility with code that checks this
        public bool IsTargetDatabaseInSync()
        {
            var pending = GetPendingMigrationsForTarget();
            return pending == null || pending.Count == 0;
        }
    }
}
