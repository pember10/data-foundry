using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Core.Services.Database
{
    /// <summary>
    /// Detects and handles data changes between target and shadow databases.
    /// </summary>
    public class ChangeDetectionService(ISqlMigrationRepository repository, ILogger logger = null)
    {
        private readonly ISqlMigrationRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        private readonly string _sqlAndPrefix = " AND ";

        internal static void ValidateIdentifier(string identifier, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identifier cannot be null or empty.", parameterName);

            if (identifier.Length > 128)
                throw new ArgumentException("Identifier exceeds maximum length of 128 characters.", parameterName);

            bool isDatabaseName = parameterName.IndexOf("database", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isDatabaseName)
            {
                if (identifier.Contains("'") || identifier.Contains("\"") || identifier.Contains(";") ||
                    identifier.Contains("--") || identifier.Contains("/*") || identifier.Contains("*/") ||
                    identifier.Contains("xp_") || identifier.Contains("sp_"))
                {
                    throw new ArgumentException("Identifier contains invalid or dangerous characters.", parameterName);
                }
            }
            else
            {
                var cleanIdentifier = identifier.Trim('[', ']');
                if (!Regex.IsMatch(cleanIdentifier, Constants.RegularExpressions.SqlIdentifier))
                    throw new ArgumentException("Identifier contains invalid characters. Only alphanumeric, underscore, and brackets allowed.", parameterName);
            }
        }

        internal static string QuoteIdentifier(string identifier)
        {
            return "[" + identifier.Replace("]", "]]") + "]";
        }

        public TableChangeSummary GetTableDiffCounts(string targetDatabase, string shadowDatabase, string table)
        {
            ValidateIdentifier(targetDatabase, nameof(targetDatabase));
            ValidateIdentifier(shadowDatabase, nameof(shadowDatabase));
            ValidateIdentifier(table, nameof(table));

            var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);

            if (pk == null || pk.Count == 0)
                return GetChangeCountsWithoutPrimaryKey(targetDatabase, shadowDatabase, table);

            var pkJoin = string.Join(_sqlAndPrefix, pk.Select(col => $"t.{QuoteIdentifier(col)} = s.{QuoteIdentifier(col)}"));
            var pkJoinReverse = string.Join(_sqlAndPrefix, pk.Select(col => $"s.{QuoteIdentifier(col)} = t.{QuoteIdentifier(col)}"));

            var nonPk = _repository.GetNonPrimaryColumns(targetDatabase, table, pk);
            string dataCompare = "1=1";

            if (nonPk.Count > 0)
            {
                var targetHash = string.Join(", '|', ", nonPk.Select(c => $"ISNULL(CONVERT(NVARCHAR(MAX), t.{QuoteIdentifier(c)}), '#NULL#')"));
                var shadowHash = string.Join(", '|', ", nonPk.Select(c => $"ISNULL(CONVERT(NVARCHAR(MAX), s.{QuoteIdentifier(c)}), '#NULL#')"));

                if (nonPk.Count > 1)
                {
                    targetHash = $"CONCAT({targetHash})";
                    shadowHash = $"CONCAT({shadowHash})";
                }

                dataCompare = $"HASHBYTES('SHA2_256', {targetHash}) <> HASHBYTES('SHA2_256', {shadowHash})";
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
                return new TableChangeSummary { Table = table, Inserts = 0, Updates = 0, Deletes = 0 };

            return new TableChangeSummary
            {
                Table = $"[dbo].[{table}]",
                Inserts = Convert.ToInt32(result.Rows[0]["Inserts"]),
                Updates = Convert.ToInt32(result.Rows[0]["Updates"]),
                Deletes = Convert.ToInt32(result.Rows[0]["Deletes"])
            };
        }

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
                Table = $"[dbo].[{table}]",
                Inserts = difference > 0 ? difference : 0,
                Updates = 0,
                Deletes = difference < 0 ? Math.Abs(difference) : 0
            };
        }

        public List<TableChangeSummary> GetChangesSummary(string targetDatabase, string shadowDatabase, List<string> tables)
        {
            var results = new List<TableChangeSummary>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var table in tables)
            {
                var tableStart = sw.ElapsedMilliseconds;
                results.Add(GetTableDiffCounts(targetDatabase, shadowDatabase, table));
                var tableTime = sw.ElapsedMilliseconds - tableStart;

                if (tableTime > 1000)
                    System.Diagnostics.Debug.WriteLine($"Table {table} took {tableTime}ms to analyze");
            }

            return results;
        }

        public void RevertChanges(string targetDatabase, string shadowDatabase, List<string> tables)
        {
            ValidateIdentifier(targetDatabase, nameof(targetDatabase));
            ValidateIdentifier(shadowDatabase, nameof(shadowDatabase));

            foreach (var table in tables)
            {
                ValidateIdentifier(table, nameof(table));
                var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);

                if (pk.Count > 0)
                {
                    RemoveRowsNotInShadow(targetDatabase, shadowDatabase, table, pk);
                }
                else
                {
                    try
                    {
                        _repository.ExecuteNonQuery(targetDatabase, $"TRUNCATE TABLE {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}");
                        _repository.ExecuteNonQuery(targetDatabase, $"INSERT INTO {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} SELECT * FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}");
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning($"Revert of [dbo].[{table}] (no PK) partially failed: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private void RemoveRowsNotInShadow(string targetDatabase, string shadowDatabase, string table, List<string> pk)
        {
            foreach (var col in pk)
                ValidateIdentifier(col, "primaryKeyColumn");

            var pkJoinExists = string.Join(_sqlAndPrefix, pk.Select(col =>
                $"{QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)}.{QuoteIdentifier(col)} = {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)}.{QuoteIdentifier(col)}"));
            var deleteSql = $"DELETE FROM {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} WHERE NOT EXISTS (SELECT 1 FROM {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} WHERE {pkJoinExists})";

            try
            {
                _repository.ExecuteNonQuery(targetDatabase, deleteSql);
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"Revert DELETE of [dbo].[{table}] failed: {ex.Message}");
                throw;
            }

            var columnsQuery = $"SELECT name FROM {QuoteIdentifier(shadowDatabase)}.sys.columns WHERE [object_id] = (SELECT [object_id] FROM {QuoteIdentifier(shadowDatabase)}.sys.[tables] WHERE [name] = @tableName)";
            var colResult = _repository.ExecuteQuery(shadowDatabase, columnsQuery);
            var sourceCols = colResult.Rows.Cast<DataRow>().Select(row => row["name"].ToString()).ToList();

            foreach (var col in sourceCols)
                ValidateIdentifier(col, "columnName");

            var colList = string.Join(", ", sourceCols.Select(c => QuoteIdentifier(c)));
            var onClause = string.Join(_sqlAndPrefix, pk.Select(c => $"TARGET.{QuoteIdentifier(c)}=SOURCE.{QuoteIdentifier(c)}"));
            var updateSet = string.Join(", ", sourceCols.Where(c => !pk.Contains(c)).Select(c => $"TARGET.{QuoteIdentifier(c)} = SOURCE.{QuoteIdentifier(c)}"));

            var mergeSql = $@"
MERGE {QuoteIdentifier(targetDatabase)}.dbo.{QuoteIdentifier(table)} AS TARGET USING {QuoteIdentifier(shadowDatabase)}.dbo.{QuoteIdentifier(table)} AS SOURCE ON ({onClause})
WHEN MATCHED THEN UPDATE SET {updateSet}
WHEN NOT MATCHED BY TARGET THEN INSERT ({colList}) VALUES ({colList});";

            try
            {
                _repository.ExecuteNonQuery(targetDatabase, mergeSql);
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"Revert MERGE of [dbo].[{table}] failed: {ex.Message}");
                throw;
            }
        }
    }
}
