using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using data_foundry.Models;

namespace data_foundry.Services
{
    /// <summary>
    /// Parses PowerShell script output into C# models.
    /// </summary>
    public static class PowerShellOutputParser
    {
        /// <summary>
        /// Parses table changes summary from PowerShell output.
        /// Expected format:
        /// Table      Inserts Updates Deletes
        /// -----      ------- ------- -------
        /// Users      5       2       1
        /// </summary>
        public static List<TableChangeSummary> ParseChanges(List<string> output)
        {
            var results = new List<TableChangeSummary>();
            var inTable = false;

            foreach (var line in output)
            {
                // Look for table header
                if (line.Contains("Table") && line.Contains("Inserts") && line.Contains("Updates") && line.Contains("Deletes"))
                {
                    inTable = true;
                    continue;
                }

                // Skip separator line
                if (line.StartsWith("-----"))
                    continue;

                // Parse data rows
                if (inTable)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        results.Add(new TableChangeSummary
                        {
                            Table = parts[0],
                            Inserts = int.TryParse(parts[1], out var ins) ? ins : 0,
                            Updates = int.TryParse(parts[2], out var upd) ? upd : 0,
                            Deletes = int.TryParse(parts[3], out var del) ? del : 0
                        });
                    }
                    else if (parts.Length > 0 && !parts[0].StartsWith("--"))
                    {
                        // End of table
                        inTable = false;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Parses pending migrations from PowerShell output.
        /// Expected format:
        /// Pending migrations:
        ///   -> 001_US123456_1_AddUsers.sql [guid]
        /// </summary>
        public static List<MigrationInfo> ParsePendingMigrations(List<string> output)
        {
            var results = new List<MigrationInfo>();
            var inPendingSection = false;

            foreach (var line in output)
            {
                if (line.Contains("Pending migrations:"))
                {
                    inPendingSection = true;
                    continue;
                }

                if (inPendingSection && line.TrimStart().StartsWith("->"))
                {
                    // Line format: "  -> 001_US123456_1_AddUsers.sql [guid]"
                    var match = Regex.Match(line, @"->\s+(.+\.sql)\s+\[([0-9a-fA-F-]+)\]");
                    if (match.Success)
                    {
                        results.Add(new MigrationInfo
                        {
                            FileName = match.Groups[1].Value.Trim(),
                            Id = Guid.Parse(match.Groups[2].Value)
                        });
                    }
                }

                if (inPendingSection && string.IsNullOrWhiteSpace(line))
                {
                    inPendingSection = false;
                }
            }

            return results;
        }

        /// <summary>
        /// Parses generated script path from PowerShell output.
        /// Expected format:
        /// Generated migration script: D:\...\script.sql
        /// </summary>
        public static string ParseGeneratedScriptPath(List<string> output)
        {
            foreach (var line in output)
            {
                if (line.Contains("Generated migration script:"))
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        return parts[1].Trim();
                    }
                }
            }

            return null;
        }
    }
}
