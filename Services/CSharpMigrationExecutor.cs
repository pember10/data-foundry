using System;
using System.Collections.Generic;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using data_foundry.Options;
using EnvDTE;

namespace data_foundry.Services
{
    /// <summary>
    /// Executes migrations using direct C# SQL operations (wraps existing SqlMigrationOrchestrator).
    /// This is the default execution strategy.
    /// </summary>
    public class CSharpMigrationExecutor : IMigrationExecutor
    {
        private readonly SqlMigrationOrchestrator _orchestrator;

        public CSharpMigrationExecutor(DataFoundryOptions options, DTE environment)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            // Delegate to existing orchestrator implementation
            _orchestrator = new SqlMigrationOrchestrator(options, environment);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation, Action<string> logger)
        {
            _orchestrator.ExecuteTargetMigrations(requireConfirmation, logger);
        }

        public List<TableChangeSummary> DetectAndHandleChanges(string action, Action<string> logger)
        {
            MigrationAction? migrationAction = null;

            if (!string.IsNullOrEmpty(action))
            {
                switch (action.ToLowerInvariant())
                {
                    case "migrate":
                        migrationAction = MigrationAction.Migrate;
                        break;
                    case "revert":
                        migrationAction = MigrationAction.Revert;
                        break;
                    case "cancel":
                        migrationAction = MigrationAction.Cancel;
                        break;
                }
            }

            return _orchestrator.DetectAndHandleChanges(migrationAction, logger);
        }

        public List<MigrationInfo> GetPendingMigrationsForTarget()
        {
            return _orchestrator.GetPendingMigrationsForTarget();
        }

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
        {
            return _orchestrator.GenerateMigrationScriptWithName(tableNames, scriptName);
        }

        public void RevertChanges(List<string> tableNames, Action<string> logger)
        {
            // Delegate to orchestrator - it will call DetectAndHandleChanges with Revert action
            _orchestrator.DetectAndHandleChanges(MigrationAction.Revert, logger);
        }
    }
}
