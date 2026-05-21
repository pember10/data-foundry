using System.Collections.Generic;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services;
using WTW.Diffusion.Core.Services.Migration;

namespace data_foundry.Services
{
    public class CSharpMigrationExecutor : IMigrationExecutor
    {
        private readonly SqlMigrationOrchestrator _orchestrator;

        public CSharpMigrationExecutor(
            ILogger logger,
            IConfigurationProvider configuration,
            IProjectManager projectManager,
            string migrationLogSchemaPath = null,
            string shadowCachePath = null)
        {
            _orchestrator = new SqlMigrationOrchestrator(
                logger, configuration, projectManager,
                migrationLogSchemaPath, shadowCachePath);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation)
            => _orchestrator.ExecuteTargetMigrations(requireConfirmation);

        public List<TableChangeSummary> DetectAndHandleChanges(string action)
        {
            MigrationAction? migrationAction = null;
            if (!string.IsNullOrEmpty(action))
                switch (action.ToLowerInvariant())
                {
                    case "migrate": migrationAction = MigrationAction.Migrate; break;
                    case "revert":  migrationAction = MigrationAction.Revert;  break;
                    case "cancel":  migrationAction = MigrationAction.Cancel;  break;
                }
            return _orchestrator.DetectAndHandleChanges(migrationAction);
        }

        public List<MigrationInfo> GetPendingMigrationsForTarget()
            => _orchestrator.GetPendingMigrationsForTarget();

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
            => _orchestrator.GenerateMigrationScriptWithName(tableNames, scriptName);

        public void RevertChanges(List<string> tableNames)
            => _orchestrator.RevertChanges(tableNames);
    }
}