using System;
using System.Collections.Generic;
using System.Data;

namespace WTW.Diffusion.Core.Services.Database
{
    /// <summary>
    /// Abstraction over SqlMigrationRepository for testability.
    /// </summary>
    public interface ISqlMigrationRepository
    {
        DataTable ExecuteQuery(string database, string query);
        int ExecuteNonQuery(string database, string query);
        bool DatabaseExists(string database);
        void CreateDatabaseIfMissing(string database);
        void EnsureMigrationLogTable(string database, string schemaScriptPath);
        void ExecuteSqlScript(string database, string script);
        void ExecuteScriptAndLog(string database, string script, Guid migrationId, string displayName, string checksum);
        List<Guid> GetExecutedMigrationIds(string database);
        void LogMigrationExecution(string database, Guid migrationId, string fileName, string checksum);
        void DropAndRecreateDatabase(string database);
        List<string> GetPrimaryKeyColumns(string database, string table);
        List<string> GetNonPrimaryColumns(string database, string table, List<string> primaryKeys);
        DataTable GetColumnMetadata(string database, string table);
        DataTable GetTableData(string database, string table);
    }
}
