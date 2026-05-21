using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace WTW.Diffusion.Core.Services.Database
{
    /// <summary>
    /// Handles all direct SQL Server database operations for migrations.
    /// </summary>
    public class SqlMigrationRepository(string targetServer, string accessToken = null) : ISqlMigrationRepository
    {
        private readonly string _targetServer = targetServer ?? throw new ArgumentNullException(nameof(targetServer));

        public DataTable ExecuteQuery(string database, string query)
        {
            var dataTable = new DataTable();
            using (var connection = OpenConnection(database))
            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);
            }
            return dataTable;
        }

        public DataTable ExecuteQuery(string database, string query, params SqlParameter[] parameters)
        {
            var dataTable = new DataTable();
            using (var connection = OpenConnection(database))
            using (var command = new SqlCommand(query, connection))
            {
                command.CommandTimeout = 300;
                if (parameters?.Length > 0)
                    command.Parameters.AddRange(parameters);
                using var adapter = new SqlDataAdapter(command);
                adapter.Fill(dataTable);
            }
            return dataTable;
        }

        public int ExecuteNonQuery(string database, string query)
        {
            using var connection = OpenConnection(database);
            using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 300;
            return command.ExecuteNonQuery();
        }

        public int ExecuteNonQuery(string database, string query, params SqlParameter[] parameters)
        {
            using var connection = OpenConnection(database);
            using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 300;
            if (parameters?.Length > 0)
                command.Parameters.AddRange(parameters);
            return command.ExecuteNonQuery();
        }

        public bool DatabaseExists(string database)
        {
            var query = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END AS exists_flag;";
            var result = ExecuteQuery("master", query, new SqlParameter("@DatabaseName", database));
            return result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["exists_flag"]) == 1;
        }

        public void CreateDatabaseIfMissing(string database)
        {
            if (DatabaseExists(database))
                return;

            var safeDatabaseName = QuoteSqlIdentifier(database);
            ExecuteNonQuery("master", $"CREATE DATABASE {safeDatabaseName};");

            if (!DatabaseExists(database))
                throw new InvalidOperationException($"Database '{database}' does not exist after attempted creation.");
        }

        public void EnsureMigrationLogTable(string database, string schemaScriptPath)
        {
            var query = "SELECT CASE WHEN OBJECT_ID('[dbo].[__MigrationLog]') IS NULL THEN 0 ELSE 1 END AS exists_flag;";
            var result = ExecuteQuery(database, query);

            if (result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["exists_flag"]) == 0)
            {
                var ddl = File.ReadAllText(schemaScriptPath);
                ExecuteSqlScript(database, ddl);
            }
        }

        public void ExecuteSqlScript(string database, string script)
        {
            var batches = Regex.Split(
                script,
                @"^\s*GO\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            foreach (var batch in batches)
            {
                var trimmedBatch = batch.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedBatch))
                    ExecuteNonQuery(database, trimmedBatch);
            }
        }

        public List<Guid> GetExecutedMigrationIds(string database)
        {
            try
            {
                var checkTableQuery = "SELECT CASE WHEN OBJECT_ID('[dbo].[__MigrationLog]') IS NULL THEN 0 ELSE 1 END AS exists_flag;";
                var tableCheckResult = ExecuteQuery(database, checkTableQuery);

                if (tableCheckResult.Rows.Count == 0 || Convert.ToInt32(tableCheckResult.Rows[0]["exists_flag"]) == 0)
                    return [];

                var query = "SELECT migration_id FROM dbo.__MigrationLog";
                var result = ExecuteQuery(database, query);
                return [.. result.Rows.Cast<DataRow>().Select(row => Guid.Parse(row["migration_id"].ToString()))];
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return [];
            }
        }

        public void LogMigrationExecution(string database, Guid migrationId, string fileName, string checksum)
        {
            var sql = @"
INSERT INTO dbo.__MigrationLog (migration_id, script_checksum, script_filename, complete_dt, applied_by, deployed, version, package_version, release_version)
VALUES (@MigrationId, @Checksum, @FileName, SYSDATETIME(), SYSTEM_USER, 1, NULL, NULL, NULL);";

            ExecuteNonQuery(database, sql,
                new SqlParameter("@MigrationId", migrationId),
                new SqlParameter("@Checksum", checksum),
                new SqlParameter("@FileName", fileName));
        }

        public void DropAndRecreateDatabase(string database)
        {
            if (DatabaseExists(database))
            {
                var safeDatabaseName = QuoteSqlIdentifier(database);
                ExecuteNonQuery("master", $"ALTER DATABASE {safeDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {safeDatabaseName};");
            }
            CreateDatabaseIfMissing(database);
        }

        public List<string> GetPrimaryKeyColumns(string database, string table)
        {
            var query = @"
SELECT c.name FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id
WHERE i.is_primary_key=1 AND OBJECT_NAME(i.object_id)=@TableName 
ORDER BY ic.key_ordinal;";

            var result = ExecuteQuery(database, query, new SqlParameter("@TableName", table));
            return [.. result.Rows.Cast<DataRow>().Select(row => row["name"].ToString())];
        }

        public List<string> GetNonPrimaryColumns(string database, string table, List<string> primaryKeys)
        {
            var pkList = string.Join("','", primaryKeys.Select(pk => pk.Replace("'", "''")));
            var query = $@"
SELECT name 
FROM sys.columns 
WHERE OBJECT_ID = OBJECT_ID(@TableName) 
  AND name NOT IN ('{pkList}')
ORDER BY column_id";

            var result = ExecuteQuery(database, query, new SqlParameter("@TableName", table));
            return [.. result.Rows.Cast<DataRow>().Select(row => row["name"].ToString())];
        }

        public DataTable GetColumnMetadata(string database, string table)
        {
            var safeDatabaseName = QuoteSqlIdentifier(database);
            var safeTableName = QuoteSqlIdentifier(table);
            var query = $@"
SELECT c.name AS ColumnName, t.name AS TypeName
FROM {safeDatabaseName}.sys.columns c
JOIN {safeDatabaseName}.sys.types t ON c.user_type_id=t.user_type_id
WHERE c.object_id = OBJECT_ID('{safeDatabaseName}.dbo.{safeTableName}')
ORDER BY c.column_id;";

            return ExecuteQuery(database, query);
        }

        public int GetServerMajorVersion(string database)
        {
            var result = ExecuteQuery(database, "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) AS MajorVersion");
            if (result.Rows.Count == 0 || result.Rows[0]["MajorVersion"] == DBNull.Value)
                return 0;
            return Convert.ToInt32(result.Rows[0]["MajorVersion"]);
        }

        public DataTable GetTableData(string database, string table)
        {
            var safeDatabaseName = QuoteSqlIdentifier(database);
            var safeTableName = QuoteSqlIdentifier(table);
            return ExecuteQuery(database, $"SELECT * FROM {safeDatabaseName}.dbo.{safeTableName}");
        }

        private static string QuoteSqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            identifier = identifier.Trim();
            if (identifier.StartsWith("[") && identifier.EndsWith("]"))
                identifier = identifier.Substring(1, identifier.Length - 2);

            identifier = identifier.Replace("]", "]]");
            return $"[{identifier}]";
        }

        /// <summary>
        /// Executes a migration script and writes the log entry within a single transaction.
        /// If the script fails, the transaction is rolled back and the migration ID is NOT logged,
        /// allowing the user to fix the script and retry cleanly.
        /// </summary>
        public void ExecuteScriptAndLog(string database, string script, Guid migrationId, string displayName, string checksum)
        {
            var batches = Regex.Split(
                script,
                @"^\s*GO\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline)
                .Select(b => b.Trim())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .ToList();

            using var connection = OpenConnection(database);
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var batch in batches)
                {
                    using var command = new SqlCommand(batch, connection, transaction);
                    command.CommandTimeout = 300;
                    command.ExecuteNonQuery();
                }

                var logSql = @"
INSERT INTO dbo.__MigrationLog (migration_id, script_checksum, script_filename, complete_dt, applied_by, deployed, version, package_version, release_version)
VALUES (@MigrationId, @Checksum, @FileName, SYSDATETIME(), SYSTEM_USER, 1, NULL, NULL, NULL);";

                using var logCommand = new SqlCommand(logSql, connection, transaction);
                logCommand.Parameters.AddWithValue("@MigrationId", migrationId);
                logCommand.Parameters.AddWithValue("@Checksum", checksum);
                logCommand.Parameters.AddWithValue("@FileName", displayName);
                logCommand.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private SqlConnection OpenConnection(string database)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = _targetServer,
                InitialCatalog = database,
                IntegratedSecurity = string.IsNullOrWhiteSpace(accessToken),
                MultipleActiveResultSets = true,
                ConnectTimeout = 30
            };

            var connection = new SqlConnection(builder.ConnectionString);
            if (!string.IsNullOrWhiteSpace(accessToken))
                connection.AccessToken = accessToken;

            connection.Open();
            return connection;
        }
    }
}
