using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace data_foundry.Services
{
    /// <summary>
    /// Handles all direct SQL Server database operations for migrations.
    /// </summary>
    public class SqlMigrationRepository
    {
        private readonly string _targetServer;
        private readonly string _accessToken;

        public SqlMigrationRepository(string targetServer, string accessToken = null)
        {
            _targetServer = targetServer ?? throw new ArgumentNullException(nameof(targetServer));
            _accessToken = accessToken;
        }

        /// <summary>
        /// Executes a SQL command against the specified database.
        /// </summary>
        public DataTable ExecuteQuery(string database, string query)
        {
            var connectionString = BuildConnectionString(database);
            var dataTable = new DataTable();

            using (var connection = new SqlConnection(connectionString))
            {
                if (!string.IsNullOrWhiteSpace(_accessToken))
                {
                    connection.AccessToken = _accessToken;
                }

                connection.Open();

                using (var command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 300; // 5 minutes
                    using (var adapter = new SqlDataAdapter(command))
                    {
                        adapter.Fill(dataTable);
                    }
                }
            }

            return dataTable;
        }

        /// <summary>
        /// Executes a SQL command with parameters against the specified database.
        /// </summary>
        public DataTable ExecuteQuery(string database, string query, params SqlParameter[] parameters)
        {
            var connectionString = BuildConnectionString(database);
            var dataTable = new DataTable();

            using (var connection = new SqlConnection(connectionString))
            {
                if (!string.IsNullOrWhiteSpace(_accessToken))
                {
                    connection.AccessToken = _accessToken;
                }

                connection.Open();

                using (var command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 300;
                    if (parameters != null && parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    using (var adapter = new SqlDataAdapter(command))
                    {
                        adapter.Fill(dataTable);
                    }
                }
            }

            return dataTable;
        }

        /// <summary>
        /// Executes a non-query SQL command (INSERT, UPDATE, DELETE, etc.).
        /// </summary>
        public int ExecuteNonQuery(string database, string query)
        {
            var connectionString = BuildConnectionString(database);

            using (var connection = new SqlConnection(connectionString))
            {
                if (!string.IsNullOrWhiteSpace(_accessToken))
                {
                    connection.AccessToken = _accessToken;
                }

                connection.Open();

                using (var command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 300;
                    return command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Executes a non-query SQL command with parameters (INSERT, UPDATE, DELETE, etc.).
        /// </summary>
        public int ExecuteNonQuery(string database, string query, params SqlParameter[] parameters)
        {
            var connectionString = BuildConnectionString(database);

            using (var connection = new SqlConnection(connectionString))
            {
                if (!string.IsNullOrWhiteSpace(_accessToken))
                {
                    connection.AccessToken = _accessToken;
                }

                connection.Open();

                using (var command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 300;
                    if (parameters != null && parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    return command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Checks if a database exists.
        /// </summary>
        public bool DatabaseExists(string database)
        {
            var query = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END AS exists_flag;";
            var result = ExecuteQuery("master", query, new SqlParameter("@DatabaseName", database));
            return result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0]["exists_flag"]) == 1;
        }

        /// <summary>
        /// Creates a database if it doesn't exist.
        /// </summary>
        public void CreateDatabaseIfMissing(string database)
        {
            if (DatabaseExists(database))
                return;

            // Database names cannot be parameterized, so we validate and quote the identifier
            var safeDatabaseName = QuoteSqlIdentifier(database);
            ExecuteNonQuery("master", $"CREATE DATABASE {safeDatabaseName};");

            if (!DatabaseExists(database))
            {
                throw new InvalidOperationException($"Database '{database}' does not exist after attempted creation.");
            }
        }

        /// <summary>
        /// Ensures the migration log table exists in the specified database.
        /// </summary>
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

        /// <summary>
        /// Executes a SQL script that may contain GO batch separators.
        /// </summary>
        public void ExecuteSqlScript(string database, string script)
        {
            // Split the script by GO statements (case-insensitive, standalone on a line)
            var batches = System.Text.RegularExpressions.Regex.Split(
                script,
                @"^\s*GO\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline
            );

            foreach (var batch in batches)
            {
                var trimmedBatch = batch.Trim();
                
                // Skip empty batches
                if (string.IsNullOrWhiteSpace(trimmedBatch))
                    continue;

                ExecuteNonQuery(database, trimmedBatch);
            }
        }

        /// <summary>
        /// Gets all executed migration IDs from the migration log.
        /// Returns empty list if the migration log table doesn't exist yet.
        /// </summary>
        public List<Guid> GetExecutedMigrationIds(string database)
        {
            try
            {
                // First check if the migration log table exists
                var checkTableQuery = "SELECT CASE WHEN OBJECT_ID('[dbo].[__MigrationLog]') IS NULL THEN 0 ELSE 1 END AS exists_flag;";
                var tableCheckResult = ExecuteQuery(database, checkTableQuery);
                
                if (tableCheckResult.Rows.Count == 0 || Convert.ToInt32(tableCheckResult.Rows[0]["exists_flag"]) == 0)
                {
                    // Table doesn't exist yet - return empty list (all migrations are pending)
                    return new List<Guid>();
                }
                
                // Table exists - get executed migrations
                var query = "SELECT migration_id FROM dbo.__MigrationLog";
                var result = ExecuteQuery(database, query);
                
                return result.AsEnumerable()
                    .Select(row => Guid.Parse(row["migration_id"].ToString()))
                    .ToList();
            }
            catch (SqlException ex) when (ex.Number == 208) // Invalid object name
            {
                // Table doesn't exist - return empty list
                return new List<Guid>();
            }
        }

        /// <summary>
        /// Logs a migration execution to the migration log table.
        /// </summary>
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

        /// <summary>
        /// Drops and recreates a database.
        /// </summary>
        public void DropAndRecreateDatabase(string database)
        {
            if (DatabaseExists(database))
            {
                // Database names cannot be parameterized, so we validate and quote the identifier
                var safeDatabaseName = QuoteSqlIdentifier(database);
                ExecuteNonQuery("master", $"ALTER DATABASE {safeDatabaseName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {safeDatabaseName};");
            }

            CreateDatabaseIfMissing(database);
        }

        /// <summary>
        /// Gets primary key columns for a table.
        /// </summary>
        public List<string> GetPrimaryKeyColumns(string database, string table)
        {
            var query = @"
SELECT c.name FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id
WHERE i.is_primary_key=1 AND OBJECT_NAME(i.object_id)=@TableName 
ORDER BY ic.key_ordinal;";

            var result = ExecuteQuery(database, query, new SqlParameter("@TableName", table));
            return result.AsEnumerable().Select(row => row["name"].ToString()).ToList();
        }

        /// <summary>
        /// Gets non-primary key columns for a table.
        /// </summary>
        public List<string> GetNonPrimaryColumns(string database, string table, List<string> primaryKeys)
        {
            // Optimized: Get columns in a single query using NOT IN
            var pkList = string.Join("','", primaryKeys.Select(pk => pk.Replace("'", "''")));
            
            var query = $@"
SELECT name 
FROM sys.columns 
WHERE OBJECT_ID = OBJECT_ID(@TableName) 
  AND name NOT IN ('{pkList}')
ORDER BY column_id";

            var result = ExecuteQuery(database, query, new SqlParameter("@TableName", table));

            return result.AsEnumerable()
                .Select(row => row["name"].ToString())
                .ToList();
        }

        /// <summary>
        /// Gets all columns with their types for a table.
        /// </summary>
        public DataTable GetColumnMetadata(string database, string table)
        {
            // Use QUOTENAME for database and table identifiers in dynamic SQL
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

        /// <summary>
        /// Gets all rows from a table.
        /// </summary>
        public DataTable GetTableData(string database, string table)
        {
            // Use QUOTENAME for database and table identifiers in dynamic SQL
            var safeDatabaseName = QuoteSqlIdentifier(database);
            var safeTableName = QuoteSqlIdentifier(table);
            
            return ExecuteQuery(database, $"SELECT * FROM {safeDatabaseName}.dbo.{safeTableName}");
        }

        /// <summary>
        /// Safely quotes a SQL identifier to prevent SQL injection.
        /// Uses SQL Server's QUOTENAME functionality via a query.
        /// </summary>
        private static string QuoteSqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", nameof(identifier));

            // Remove any existing brackets to prevent double-quoting
            identifier = identifier.Trim('[', ']');

            // Validate identifier doesn't contain invalid characters
            if (identifier.Contains("]"))
                throw new ArgumentException("Invalid identifier: contains ']' character.", nameof(identifier));

            return $"[{identifier}]";
        }

        private string BuildConnectionString(string database)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = _targetServer,
                InitialCatalog = database,
                IntegratedSecurity = string.IsNullOrWhiteSpace(_accessToken),
                MultipleActiveResultSets = true,
                ConnectTimeout = 30
            };

            return builder.ConnectionString;
        }
    }
}
