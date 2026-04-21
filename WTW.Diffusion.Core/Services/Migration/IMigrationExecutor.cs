using System;
using System.Collections.Generic;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Core.Services.Migration
{
    /// <summary>
    /// Interface for migration execution strategies (C# or PowerShell).
    /// </summary>
    public interface IMigrationExecutor
    {
        void ExecuteTargetMigrations(bool requireConfirmation, Action<string> logger);
        List<TableChangeSummary> DetectAndHandleChanges(string action, Action<string> logger);
        List<MigrationInfo> GetPendingMigrationsForTarget();
        string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName);
        void RevertChanges(List<string> tableNames, Action<string> logger);
    }
}
