using System;
using System.Collections.Generic;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using data_foundry.Options;
using EnvDTE;

namespace data_foundry.Services
{
    public class CSharpMigrationExecutor : IMigrationExecutor
    {
        private readonly SqlMigrationOrchestrator _orchestrator;

        public CSharpMigrationExecutor(DataFoundryOptions options, DTE environment)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            _orchestrator = new SqlMigrationOrchestrator(options, environment);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation, ILogger logger = null)
            => _orchestrator.ExecuteTargetMigrations(requireConfirmation, logger);

        public List<TableChangeSummary> DetectAndHandleChanges(string action, ILogger logger = null)
        {
            MigrationAction? migrationAction = null;
            if (!string.IsNullOrEmpty(action))
                switch (action.ToLowerInvariant())
                {
                    case "migrate": migrationAction = MigrationAction.Migrate; break;
                    case "revert":  migrationAction = MigrationAction.Revert;  break;
                    case "cancel":  migrationAction = MigrationAction.Cancel;  break;
                }
            return _orchestrator.DetectAndHandleChanges(migrationAction, logger);
        }

        public List<MigrationInfo> GetPendingMigrationsForTarget()
            => _orchestrator.GetPendingMigrationsForTarget();

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
            => _orchestrator.GenerateMigrationScriptWithName(tableNames, scriptName);

        public void RevertChanges(List<string> tableNames, ILogger logger = null)
            => _orchestrator.DetectAndHandleChanges(MigrationAction.Revert, logger);
    }
}