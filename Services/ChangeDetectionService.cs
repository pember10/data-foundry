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

            // SQL identifiers: alphanumeric, underscore, no spaces or special characters
            if (!Regex.IsMatch(identifier, Constants.RegularExpressions.SqlIdentifier))
            {
                throw new ArgumentException($"Identifier contains invalid characters. Only alphanumeric and underscore allowed.", parameterName);
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
        /// </summary>
        public TableChangeSummary GetTableDiffCounts(string targetDatabase, string shadowDatabase, string table)
        {
            // Validate all identifiers
            ValidateIdentifier(targetDatabase, nameof(targetDatabase));
            ValidateIdentifier(shadowDatabase, nameof(shadowDatabase));
            ValidateIdentifier(table, nameof(table));

            var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);
            int updateCount = 0;

            if (pk != null && pk.Count > 0)
            {
                updateCount = GetUpdateCount(targetDatabase, shadowDatabase, table, pk, updateCount);
            }

            // Count inserts and deletes using EXCEPT
            var insertSql = $"SELECT COUNT(*) AS c FROM (SELECT * FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} EXCEPT SELECT * FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}) x";
            var deleteSql = $"SELECT COUNT(*) AS c FROM (SELECT * FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} EXCEPT SELECT * FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}) x";

            var insertResult = _repository.ExecuteQuery(targetDatabase, insertSql);
            var deleteResult = _repository.ExecuteQuery(targetDatabase, deleteSql);

            var insertCount = insertResult.Rows.Count > 0 ? Convert.ToInt32(insertResult.Rows[0]["c"]) - updateCount : 0;
            var deleteCount = deleteResult.Rows.Count > 0 ? Convert.ToInt32(deleteResult.Rows[0]["c"]) - updateCount : 0;

            return new TableChangeSummary
            {
                Table = table,
                Inserts = insertCount,
                Updates = updateCount,
                Deletes = deleteCount
            };
        }

        private int GetUpdateCount(string targetDatabase, string shadowDatabase, string table, List<string> pk, int updateCount)
        {
            // Validate primary key column names
            foreach (var col in pk)
            {
                ValidateIdentifier(col, "primaryKeyColumn");
            }

            var nonPk = _repository.GetNonPrimaryColumns(targetDatabase, table, pk);

            if (nonPk.Count > 0)
            {
                // Validate non-primary key column names
                foreach (var col in nonPk)
                {
                    ValidateIdentifier(col, "nonPrimaryKeyColumn");
                }

                var join = string.Join(" AND ", pk.Select(col => $"p.{QuoteIdentifier(col)}=s.{QuoteIdentifier(col)}"));
                var hashCols = string.Join(", '|' ,", nonPk.Select(col => $"ISNULL(CONVERT(nvarchar(max),p.{QuoteIdentifier(col)}),'#NULL#')"));
                var hashColsShadow = string.Join(", '|' ,", nonPk.Select(col => $"ISNULL(CONVERT(nvarchar(max),s.{QuoteIdentifier(col)}),'#NULL#')"));

                if (nonPk.Count > 1)
                {
                    hashCols = $"CONCAT({hashCols})";
                    hashColsShadow = $"CONCAT({hashColsShadow})";
                }

                var updateSql = $@"
SELECT COUNT(*) AS c FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} p
JOIN {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} s ON {join}
WHERE HASHBYTES('SHA2_256', {hashCols}) <> HASHBYTES('SHA2_256', {hashColsShadow});";

                var result = _repository.ExecuteQuery(targetDatabase, updateSql);
                updateCount = result.Rows.Count > 0 ? Convert.ToInt32(result.Rows[0]["c"]) : 0;
            }

            return updateCount;
        }

        /// <summary>
        /// Gets a summary of changes for multiple tables.
        /// </summary>
        public List<TableChangeSummary> GetChangesSummary(string targetDatabase, string shadowDatabase, List<string> tables)
        {
            var results = new List<TableChangeSummary>();

            foreach (var table in tables)
            {
                results.Add(GetTableDiffCounts(targetDatabase, shadowDatabase, table));
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
