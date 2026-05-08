using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Core.Services.Migration
{
    /// <summary>
    /// Parses PowerShell script output into C# models.
    /// </summary>
    public static class PowerShellOutputParser
    {
        public static List<TableChangeSummary> ParseChanges(List<string> output)
        {
            var results = new List<TableChangeSummary>();
            var inTable = false;

            foreach (var line in output)
            {
                if (line.Contains("Table") && line.Contains("Inserts") && line.Contains("Updates") && line.Contains("Deletes"))
                {
                    inTable = true;
                    continue;
                }

                if (line.StartsWith("-----"))
                    continue;

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
                    // Skip rows that don't have enough parts — don't exit table mode
                    // Only an empty line or a line starting with a letter (new section)
                    // after we've already seen valid rows should exit table mode.
                }
            }

            return results;
        }

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
                    inPendingSection = false;
            }

            return results;
        }

        public static string ParseGeneratedScriptPath(List<string> output)
        {
            foreach (var line in output)
            {
                if (line.Contains("Generated migration script:"))
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                        return parts[1].Trim();
                }
            }

            return null;
        }
    }
}
