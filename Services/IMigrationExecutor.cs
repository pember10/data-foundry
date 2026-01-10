using System;
using System.Collections.Generic;
using data_foundry.Models;

namespace data_foundry.Services
{
    /// <summary>
    /// Interface for migration execution strategies (C# or PowerShell).
    /// </summary>
    public interface IMigrationExecutor
    {
        /// <summary>
        /// Executes pending migrations on the target database.
        /// </summary>
        void ExecuteTargetMigrations(bool requireConfirmation, Action<string> logger);

        /// <summary>
        /// Detects changes between target and shadow databases.
        /// </summary>
        List<TableChangeSummary> DetectAndHandleChanges(string action, Action<string> logger);

        /// <summary>
        /// Gets list of pending migrations for the target database.
        /// </summary>
        List<MigrationInfo> GetPendingMigrationsForTarget();

        /// <summary>
        /// Generates a migration script with the specified name.
        /// </summary>
        string GenerateMigrationScriptWithName(List<string> tableNames, string scriptName);

        /// <summary>
        /// Reverts changes in target database to match shadow database for specified tables.
        /// </summary>
        void RevertChanges(List<string> tableNames, Action<string> logger);
    }
}
