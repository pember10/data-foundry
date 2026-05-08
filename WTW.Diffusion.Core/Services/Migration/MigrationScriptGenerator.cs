using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using WTW.Diffusion.Core.Services.Database;

namespace WTW.Diffusion.Core.Services.Migration
{
    /// <summary>
    /// Generates migration scripts from detected data changes.
    /// </summary>
    public class MigrationScriptGenerator(ISqlMigrationRepository repository)
    {
        private readonly ISqlMigrationRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        public string GenerateMigrationScript(string targetDatabase, string shadowDatabase, List<string> tables, string outputDir, string scriptName)
        {
            var guid = Guid.NewGuid();
            var fileName = $"{scriptName}.sql";
            var targetFolder = outputDir;

            var subfolders = Directory.GetDirectories(outputDir).OrderBy(d => d).ToArray();
            if (subfolders.Length > 0)
                targetFolder = subfolders[subfolders.Length - 1];

            if (!Directory.Exists(targetFolder))
                Directory.CreateDirectory(targetFolder);

            var fullPath = Path.Combine(targetFolder, fileName);
            var sb = new StringBuilder();

            sb.AppendLine($"-- <Migration ID=\"{guid}\" />");
            sb.AppendLine("GO");
            sb.AppendLine("SET DATEFORMAT YMD;");
            sb.AppendLine("GO");

            foreach (var table in tables)
                GenerateMigrationForTable(targetDatabase, shadowDatabase, sb, table);

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            return fullPath;
        }

        private void GenerateMigrationForTable(string targetDatabase, string shadowDatabase, StringBuilder sb, string table)
        {
            var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);
            var colMeta = _repository.GetColumnMetadata(targetDatabase, table);
            var allCols = colMeta.Rows.Cast<DataRow>().Select(row => row["ColumnName"].ToString()).ToList();

            if (pk == null || pk.Count == 0)
            {
                sb.AppendLine($"-- Skipped table {table}: no primary key detected at generation time.");
                return;
            }

            var targetRows = _repository.GetTableData(targetDatabase, table);
            var shadowRows = _repository.GetTableData(shadowDatabase, table);
            var targetDict = BuildRowDictionary(targetRows, pk);
            var shadowDict = BuildRowDictionary(shadowRows, pk);
            var nonPk = allCols.Where(c => !pk.Contains(c)).ToList();

            var deleteKeys = shadowDict.Keys.Where(k => !targetDict.ContainsKey(k)).ToList();
            if (deleteKeys.Count > 0)
                GenerateDeleteStatements(sb, table, pk, shadowDict, deleteKeys);

            var insertKeys = targetDict.Keys.Where(k => !shadowDict.ContainsKey(k)).ToList();
            if (insertKeys.Count > 0)
                GenerateInsertStatements(sb, table, allCols, targetDict, insertKeys);

            var updateKeys = targetDict.Keys.Where(k => shadowDict.ContainsKey(k)).ToList();
            if (updateKeys.Count > 0)
                GenerateUpdateStatements(sb, table, pk, targetDict, shadowDict, nonPk, updateKeys);
        }

        private static void GenerateUpdateStatements(StringBuilder sb, string table, List<string> pk, Dictionary<string, DataRow> targetDict, Dictionary<string, DataRow> shadowDict, List<string> nonPk, List<string> updateKeys)
        {
            var updateStatements = new List<string>();
            var quotedTable = QuoteIdentifier(table);

            foreach (var key in updateKeys)
            {
                var targetRow = targetDict[key];
                var shadowRow = shadowDict[key];
                var setClauses = new List<string>();

                foreach (var col in nonPk)
                {
                    var targetVal = targetRow[col];
                    var shadowVal = shadowRow[col];
                    if (!ValuesEqual(targetVal, shadowVal))
                        setClauses.Add($"{QuoteIdentifier(col)} = {FormatSqlLiteral(targetVal)}");
                }

                if (setClauses.Count > 0)
                {
                    var predicates = pk.Select(c => $"{QuoteIdentifier(c)} = {FormatSqlLiteral(targetRow[c])}");
                    updateStatements.Add($"UPDATE dbo.{quotedTable} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", predicates)};");
                }
            }

            if (updateStatements.Count > 0)
            {
                sb.AppendLine($"PRINT (N'Update {updateStatements.Count} row(s) in [dbo].{quotedTable}');");
                foreach (var stmt in updateStatements)
                    sb.AppendLine(stmt);
                sb.AppendLine("GO");
            }
        }

        private static void GenerateInsertStatements(StringBuilder sb, string table, List<string> allCols, Dictionary<string, DataRow> targetDict, List<string> insertKeys)
        {
            var quotedTable = QuoteIdentifier(table);
            sb.AppendLine($"PRINT (N'Add {insertKeys.Count} row(s) to [dbo].{quotedTable}');");
            foreach (var key in insertKeys)
            {
                var row = targetDict[key];
                var colList = string.Join(", ", allCols.Select(c => QuoteIdentifier(c)));
                var valList = string.Join(", ", allCols.Select(c => FormatSqlLiteral(row[c])));
                sb.AppendLine($"INSERT INTO [dbo].{quotedTable} ({colList}) VALUES ({valList});");
            }
            sb.AppendLine("GO");
        }

        private static void GenerateDeleteStatements(StringBuilder sb, string table, List<string> pk, Dictionary<string, DataRow> shadowDict, List<string> deleteKeys)
        {
            var quotedTable = QuoteIdentifier(table);
            sb.AppendLine($"PRINT (N'Delete {deleteKeys.Count} row(s) from [dbo].{quotedTable}');");
            foreach (var key in deleteKeys)
            {
                var row = shadowDict[key];
                var predicates = pk.Select(c => $"{QuoteIdentifier(c)} = {FormatSqlLiteral(row[c])}");
                sb.AppendLine($"DELETE FROM [dbo].{quotedTable} WHERE {string.Join(" AND ", predicates)};");
            }
            sb.AppendLine("GO");
        }

        private static Dictionary<string, DataRow> BuildRowDictionary(DataTable table, List<string> pkColumns)
        {
            var dict = new Dictionary<string, DataRow>();
            foreach (DataRow row in table.Rows)
            {
                var key = string.Join("||", pkColumns.Select(c => row[c]?.ToString() ?? string.Empty));
                dict[key] = row;
            }
            return dict;
        }

        private static bool ValuesEqual(object val1, object val2)
        {
            if (val1 == null || val1 is DBNull) return val2 == null || val2 is DBNull;
            if (val2 == null || val2 is DBNull) return false;
            return val1.Equals(val2);
        }

        private static string FormatSqlLiteral(object value)
        {
            if (value == null || value is DBNull) return "NULL";
            if (value is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";
            if (value is bool b) return b ? "1" : "0";
            if (value is byte[] bytes) return "0x" + BitConverter.ToString(bytes).Replace("-", "");
            if (value is string str) return $"'{str.Replace("'", "''")}'";
            if (value is Guid guid) return $"'{guid}'";
            if (value is IFormattable formattable) return formattable.ToString();
            return $"'{value.ToString().Replace("'", "''")}'";
        }

        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            identifier = identifier.Trim();
            if (identifier.StartsWith("[") && identifier.EndsWith("]"))
                identifier = identifier.Substring(1, identifier.Length - 2);
            identifier = identifier.Replace("]", "]]");
            return $"[{identifier}]";
        }
    }
}
