using System.Collections.Generic;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;
using WTW.Diffusion.Core.Services.Migration;
using data_foundry.Options;
using EnvDTE;

namespace data_foundry.Services
{
    public class PowerShellOrchestratorWrapper
    {
        private readonly IMigrationExecutor _executor;

        public PowerShellOrchestratorWrapper(DataFoundryOptions options, DTE environment)
        {
            _executor = new PowerShellMigrationExecutor(options, environment);
        }

        public void ExecuteTargetMigrations(bool requireConfirmation = false, ILogger logger = null)
            => _executor.ExecuteTargetMigrations(requireConfirmation, logger);

        public List<TableChangeSummary> DetectAndHandleChanges(MigrationAction? action = null, ILogger logger = null)
            => _executor.DetectAndHandleChanges(action.HasValue ? action.Value.ToString() : null, logger);

        public List<MigrationInfo> GetPendingMigrationsForTarget()
            => _executor.GetPendingMigrationsForTarget();

        public string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName)
            => _executor.GenerateMigrationScriptWithName(tableNames, scriptName);

        public bool IsTargetDatabaseInSync()
        {
            var pending = GetPendingMigrationsForTarget();
            return pending == null || pending.Count == 0;
        }
    }
}