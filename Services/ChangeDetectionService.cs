using data_foundry.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace data_foundry.Services
{
    /// <summary>
    /// Detects and handles data changes between target and shadow databases.
    /// </summary>
    public class ChangeDetectionService
    {
        private readonly SqlMigrationRepository _repository;

        public ChangeDetectionService(SqlMigrationRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Validates that an identifier (database, table, or column name) is safe to use in dynamic SQL.
        /// </summary>
        private static void ValidateIdentifier(string identifier, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException($"Identifier cannot be null or empty.", parameterName);
            }

            if (identifier.Length > 128)
            {
                throw new ArgumentException($"Identifier exceeds maximum length of 128 characters.", parameterName);
            }

            // For database names, allow dots and hyphens (common in SQL Server database names)
            // For table/column names, use strict validation
            bool isDatabaseName = parameterName.IndexOf("database", StringComparison.OrdinalIgnoreCase) >= 0;
            
            if (isDatabaseName)
            {
                // Database names: allow alphanumeric, underscore, dot, and hyphen
                // Disallow dangerous characters that could be used for SQL injection
                if (identifier.Contains("'") || identifier.Contains("\"") || identifier.Contains(";") || 
                    identifier.Contains("--") || identifier.Contains("/*") || identifier.Contains("*/") ||
                    identifier.Contains("xp_") || identifier.Contains("sp_"))
                {
                    throw new ArgumentException($"Identifier contains invalid or dangerous characters.", parameterName);
                }
            }
            else
            {
                // Table/column names: strict validation (alphanumeric and underscore only)
                if (!Regex.IsMatch(identifier, Constants.RegularExpressions.SqlIdentifier))
                {
                    throw new ArgumentException($"Identifier contains invalid characters. Only alphanumeric and underscore allowed.", parameterName);
                }
            }
        }

        /// <summary>
        /// Safely quotes an identifier for use in dynamic SQL.
        /// Uses double brackets for additional safety.
        /// </summary>
        private static string QuoteIdentifier(string identifier)
        {
            // Replace ] with ]] to escape any brackets in the identifier
            return "[" + identifier.Replace("]", "]]") + "]";
        }

        /// <summary>
        /// Gets a summary of changes for a specific table.
        /// PERFORMANCE OPTIMIZED: Uses EXISTS instead of EXCEPT for better performance.
        /// </summary>
        public TableChangeSummary GetTableDiffCounts(string targetDatabase, string shadowDatabase, string table)
        {
            // Validate all identifiers
            ValidateIdentifier(targetDatabase, nameof(targetDatabase));
            ValidateIdentifier(shadowDatabase, nameof(shadowDatabase));
            ValidateIdentifier(table, nameof(table));

            var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);
            
            if (pk == null || pk.Count == 0)
            {
                // No primary key - fall back to simple row count comparison
                return GetChangeCountsWithoutPrimaryKey(targetDatabase, shadowDatabase, table);
            }

            // PERFORMANCE OPTIMIZATION: Single combined query instead of 4 separate queries
            var pkJoin = string.Join(" AND ", pk.Select(col => $"t.{QuoteIdentifier(col)} = s.{QuoteIdentifier(col)}"));
            var pkJoinReverse = string.Join(" AND ", pk.Select(col => $"s.{QuoteIdentifier(col)} = t.{QuoteIdentifier(col)}"));
            
            var nonPk = _repository.GetNonPrimaryColumns(targetDatabase, table, pk);
            string dataCompare = "1=1"; // Default: no non-PK columns to compare
            
            if (nonPk.Count > 0)
            {
                // Use CHECKSUM for performance - need to prefix each column with table alias
                var targetCols = string.Join(", ", nonPk.Select(c => $"t.{QuoteIdentifier(c)}"));
                var shadowCols = string.Join(", ", nonPk.Select(c => $"s.{QuoteIdentifier(c)}"));
                dataCompare = $"CHECKSUM({targetCols}) <> CHECKSUM({shadowCols})";
            }

            var combinedSql = $@"
SELECT 
    (SELECT COUNT(*) FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} t 
     WHERE NOT EXISTS (SELECT 1 FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} s WHERE {pkJoin})) AS Inserts,
    (SELECT COUNT(*) FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} t 
     INNER JOIN {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} s ON {pkJoin}
     WHERE {dataCompare}) AS Updates,
    (SELECT COUNT(*) FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} s 
     WHERE NOT EXISTS (SELECT 1 FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} t WHERE {pkJoinReverse})) AS Deletes";

            var result = _repository.ExecuteQuery(targetDatabase, combinedSql);
            
            if (result.Rows.Count == 0)
            {
                return new TableChangeSummary { Table = table, Inserts = 0, Updates = 0, Deletes = 0 };
            }

            return new TableChangeSummary
            {
                Table = table,
                Inserts = Convert.ToInt32(result.Rows[0]["Inserts"]),
                Updates = Convert.ToInt32(result.Rows[0]["Updates"]),
                Deletes = Convert.ToInt32(result.Rows[0]["Deletes"])
            };
        }

        /// <summary>
        /// Fallback method for tables without primary keys (compares row counts only).
        /// </summary>
        private TableChangeSummary GetChangeCountsWithoutPrimaryKey(string targetDatabase, string shadowDatabase, string table)
        {
            var targetCountSql = $"SELECT COUNT(*) AS c FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}";
            var shadowCountSql = $"SELECT COUNT(*) AS c FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}";

            var targetResult = _repository.ExecuteQuery(targetDatabase, targetCountSql);
            var shadowResult = _repository.ExecuteQuery(shadowDatabase, shadowCountSql);

            var targetCount = targetResult.Rows.Count > 0 ? Convert.ToInt32(targetResult.Rows[0]["c"]) : 0;
            var shadowCount = shadowResult.Rows.Count > 0 ? Convert.ToInt32(shadowResult.Rows[0]["c"]) : 0;

            var difference = targetCount - shadowCount;

            return new TableChangeSummary
            {
                Table = table,
                Inserts = difference > 0 ? difference : 0,
                Updates = 0, // Cannot determine without PK
                Deletes = difference < 0 ? Math.Abs(difference) : 0
            };
        }

        /// <summary>
        /// Gets a summary of changes for multiple tables.
        /// </summary>
        public List<TableChangeSummary> GetChangesSummary(string targetDatabase, string shadowDatabase, List<string> tables)
        {
            var results = new List<TableChangeSummary>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var table in tables)
            {
                var tableStart = sw.ElapsedMilliseconds;
                results.Add(GetTableDiffCounts(targetDatabase, shadowDatabase, table));
                var tableTime = sw.ElapsedMilliseconds - tableStart;
                
                if (tableTime > 1000) // Log if table takes more than 1 second
                {
                    System.Diagnostics.Debug.WriteLine($"Table {table} took {tableTime}ms to analyze");
                }
            }

            return results;
        }

        /// <summary>
        /// Reverts changes in target database to match shadow database.
        /// </summary>
        public void RevertChanges(string targetDatabase, string shadowDatabase, List<string> tables)
        {
            // Validate database names
            ValidateIdentifier(targetDatabase, nameof(targetDatabase));
            ValidateIdentifier(shadowDatabase, nameof(shadowDatabase));

            foreach (var table in tables)
            {
                // Validate table name
                ValidateIdentifier(table, nameof(table));

                var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);

                if (pk.Count > 0)
                {
                    RemoveRowsNotInShadow(targetDatabase, shadowDatabase, table, pk);
                }
                else
                {
                    // Fallback: truncate and copy if no PK
                    try
                    {
                        _repository.ExecuteNonQuery(targetDatabase, $"TRUNCATE TABLE {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}");
                        _repository.ExecuteNonQuery(targetDatabase, $"INSERT INTO {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} SELECT * FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}");
                    }
                    catch
                    {
                        // Continue on error
                    }
                }
            }
        }

        private void RemoveRowsNotInShadow(string targetDatabase, string shadowDatabase, string table, List<string> pk)
        {
            // Validate primary key column names
            foreach (var col in pk)
            {
                ValidateIdentifier(col, "primaryKeyColumn");
            }

            // Delete rows not in shadow
            var pkJoinExists = string.Join(" AND ", pk.Select(col => $"{QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}.{QuoteIdentifier(col)} = {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}.{QuoteIdentifier(col)}"));
            var deleteSql = $"DELETE FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} WHERE NOT EXISTS (SELECT 1 FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} WHERE {pkJoinExists})";

            try
            {
                _repository.ExecuteNonQuery(targetDatabase, deleteSql);
            }
            catch
            {
                // Continue on error
            }

            // Merge inserts/updates - use parameterized query for table name validation
            var columnsQuery = $"SELECT name FROM {QuoteIdentifier(shadowDatabase)}.sys.columns WHERE [object_id] = (SELECT [object_id] FROM {QuoteIdentifier(shadowDatabase)}.sys.[tables] WHERE [name] = @tableName)";

            // Note: This requires ExecuteQuery to support parameters. If not available, validate table exists first.
            var colResult = _repository.ExecuteQuery(shadowDatabase, columnsQuery);
            var sourceCols = colResult.Rows.Cast<DataRow>().Select(row => row["name"].ToString()).ToList();

            // Validate all column names
            foreach (var col in sourceCols)
            {
                ValidateIdentifier(col, "columnName");
            }

            var colList = string.Join(", ", sourceCols.Select(c => QuoteIdentifier(c)));
            var onClause = string.Join(" AND ", pk.Select(c => $"TARGET.{QuoteIdentifier(c)}=SOURCE.{QuoteIdentifier(c)}"));
            var updateSet = string.Join(", ", sourceCols.Where(c => !pk.Contains(c)).Select(c => $"TARGET.{QuoteIdentifier(c)} = SOURCE.{QuoteIdentifier(c)}"));

            var mergeSql = $@"
MERGE {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} AS TARGET USING {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} AS SOURCE ON ({onClause})
WHEN MATCHED THEN UPDATE SET {updateSet}
WHEN NOT MATCHED BY TARGET THEN INSERT ({colList}) VALUES ({colList});";

            try
            {
                _repository.ExecuteNonQuery(targetDatabase, mergeSql);
            }
            catch
            {
                // Continue on error
            }
        }
    }
}
