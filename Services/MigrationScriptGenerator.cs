using data_foundry.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace data_foundry.Services
{
    /// <summary>
    /// Generates migration scripts from detected data changes.
    /// </summary>
    public class MigrationScriptGenerator
    {
        private readonly SqlMigrationRepository _repository;
        private readonly MigrationScriptManager _scriptManager;

        public MigrationScriptGenerator(SqlMigrationRepository repository, MigrationScriptManager scriptManager)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _scriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
        }

        /// <summary>
        /// Generates a migration script from differences between target and shadow databases.
        /// </summary>
        public string GenerateMigrationScript(string targetDatabase, string shadowDatabase, List<string> tables, string outputDir, string scriptName)
        {
            var guid = Guid.NewGuid();
            var fileName = $"{scriptName}.sql";
            var targetFolder = outputDir;

            // Use the latest subfolder if any exist
            var subfolders = Directory.GetDirectories(outputDir).OrderBy(d => d).ToArray();
            if (subfolders.Length > 0)
            {
                targetFolder = subfolders[subfolders.Length - 1];
            }

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            var fullPath = Path.Combine(targetFolder, fileName);
            var sb = new StringBuilder();

            sb.AppendLine($"-- <Migration ID=\"{guid}\" />");
            sb.AppendLine("GO");
            sb.AppendLine("SET DATEFORMAT YMD;");
            sb.AppendLine("GO");

            foreach (var table in tables)
            {
                GenerateMigrationForTable(targetDatabase, shadowDatabase, sb, table);
            }

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
            return fullPath;
        }

        private void GenerateMigrationForTable(string targetDatabase, string shadowDatabase, StringBuilder sb, string table)
        {
            var pk = _repository.GetPrimaryKeyColumns(targetDatabase, table);
            var colMeta = _repository.GetColumnMetadata(targetDatabase, table);
            var allCols = colMeta.AsEnumerable().Select(row => row["ColumnName"].ToString()).ToList();

            if (pk == null || pk.Count == 0)
            {
                sb.AppendLine($"-- Skipped table {table}: no primary key detected at generation time.");
                return;
            }

            // Get data from both databases
            var targetRows = _repository.GetTableData(targetDatabase, table);
            var shadowRows = _repository.GetTableData(shadowDatabase, table);

            // Create dictionaries keyed by composite PK
            var targetDict = BuildRowDictionary(targetRows, pk);
            var shadowDict = BuildRowDictionary(shadowRows, pk);
            var nonPk = allCols.Where(c => !pk.Contains(c)).ToList();

            // Generate DELETE statements
            var deleteKeys = shadowDict.Keys.Where(k => !targetDict.ContainsKey(k)).ToList();
            if (deleteKeys.Count > 0)
            {
                GenerateDeleteStatements(sb, table, pk, shadowDict, deleteKeys);
            }

            // Generate INSERT statements
            var insertKeys = targetDict.Keys.Where(k => !shadowDict.ContainsKey(k)).ToList();
            if (insertKeys.Count > 0)
            {
                GenerateInsertStatements(sb, table, allCols, targetDict, insertKeys);
            }

            // Generate UPDATE statements
            var updateKeys = targetDict.Keys.Where(k => shadowDict.ContainsKey(k)).ToList();
            if (updateKeys.Count > 0)
            {
                GenerateUpdateStatements(sb, table, pk, targetDict, shadowDict, nonPk, updateKeys);
            }
        }

        private static void GenerateUpdateStatements(StringBuilder sb, string table, List<string> pk, Dictionary<string, DataRow> targetDict, Dictionary<string, DataRow> shadowDict, List<string> nonPk, List<string> updateKeys)
        {
            var updateStatements = new List<string>();

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
                    {
                        setClauses.Add($"[{col}] = {FormatSqlLiteral(targetVal)}");
                    }
                }

                if (setClauses.Count > 0)
                {
                    var predicates = pk.Select(c => $"[{c}] = {FormatSqlLiteral(targetRow[c])}");
                    var where = string.Join(" AND ", predicates);
                    var setList = string.Join(", ", setClauses);
                    updateStatements.Add($"UPDATE dbo.[{table}] SET {setList} WHERE {where};");
                }
            }

            if (updateStatements.Count > 0)
            {
                sb.AppendLine($"PRINT (N'Update {updateStatements.Count} row(s) in [dbo].[{table}]');");
                foreach (var stmt in updateStatements)
                {
                    sb.AppendLine(stmt);
                }
                sb.AppendLine("GO");
            }
        }

        private static void GenerateInsertStatements(StringBuilder sb, string table, List<string> allCols, Dictionary<string, DataRow> targetDict, List<string> insertKeys)
        {
            sb.AppendLine($"PRINT (N'Add {insertKeys.Count} row(s) to [dbo].[{table}]');");
            foreach (var key in insertKeys)
            {
                var row = targetDict[key];
                var values = allCols.Select(c => FormatSqlLiteral(row[c]));
                var colList = string.Join(", ", allCols.Select(c => $"[{c}]"));
                var valList = string.Join(", ", values);
                sb.AppendLine($"INSERT INTO [dbo].[{table}] ({colList}) VALUES ({valList});");
            }
            sb.AppendLine("GO");
        }

        private static void GenerateDeleteStatements(StringBuilder sb, string table, List<string> pk, Dictionary<string, DataRow> shadowDict, List<string> deleteKeys)
        {
            sb.AppendLine($"PRINT (N'Delete {deleteKeys.Count} row(s) from [dbo].[{table}]');");
            foreach (var key in deleteKeys)
            {
                var row = shadowDict[key];
                var predicates = pk.Select(c => $"[{c}] = {FormatSqlLiteral(row[c])}");
                var where = string.Join(" AND ", predicates);
                sb.AppendLine($"DELETE FROM [dbo].[{table}] WHERE {where};");
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
            if (val1 == null || val1 is DBNull)
                return val2 == null || val2 is DBNull;

            if (val2 == null || val2 is DBNull)
                return false;

            return val1.Equals(val2);
        }

        private static string FormatSqlLiteral(object value)
        {
            if (value == null || value is DBNull)
                return "NULL";

            if (value is DateTime dt)
                return $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'";

            if (value is bool b)
                return b ? "1" : "0";

            if (value is byte[] bytes)
                return "0x" + BitConverter.ToString(bytes).Replace("-", "");

            if (value is string str)
                return $"'{str.Replace("'", "''")}'";

            if (value is Guid guid)
                return $"'{guid}'";

            if (value is IFormattable formattable)
                return formattable.ToString();

            return $"'{value.ToString().Replace("'", "''")}'";
        }
    }
}
