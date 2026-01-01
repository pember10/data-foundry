using data_foundry.Config;
using data_foundry.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static data_foundry.Constants;

namespace data_foundry.Services
{
    /// <summary>
    /// Manages the tracked tables configuration stored in the extension's installation directory.
    /// </summary>
    public static class TrackedTablesManager
    {
        private static string GetConfigPath()
        {
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(TrackedTablesManager));
            var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
            
            if (!string.IsNullOrEmpty(configDir))
            {
                PathHelper.EnsureDirectoryExists(configDir);
                return PathHelper.SafeCombine(configDir, "tablelist.json");
            }
            
            // Fallback: use a default location
            throw new InvalidOperationException("Failed to determine config path for tracked tables.");
        }

        /// <summary>
        /// Gets the list of tracked tables from tablelist.json in the extension directory.
        /// </summary>
        public static List<string> GetTrackedTables()
        {
            try
            {
                var configPath = GetConfigPath();

                if (!File.Exists(configPath))
                {
                    // Create default empty config
                    SaveTrackedTables(new List<string>());
                    return new List<string>();
                }

                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<TableListConfig>(json);
                return config?.Tables ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Saves the list of tracked tables to tablelist.json in the extension directory.
        /// </summary>
        public static void SaveTrackedTables(List<string> tables)
        {
            try
            {
                var configPath = GetConfigPath();
                var config = new TableListConfig { Tables = tables ?? new List<string>() };
                
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                // Ensure directory exists
                var directory = PathHelper.GetSafeDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    PathHelper.EnsureDirectoryExists(directory);
                    File.WriteAllText(configPath, json);
                }
            }
            catch (Exception ex)
            {
                // Log or handle the error appropriately
                System.Diagnostics.Debug.WriteLine($"Failed to save tracked tables: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a table to the tracked tables list if it's not already tracked.
        /// </summary>
        public static void AddTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return;

            var tables = GetTrackedTables();
            if (!tables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            {
                tables.Add(tableName);
                SaveTrackedTables(tables);
            }
        }

        /// <summary>
        /// Removes a table from the tracked tables list.
        /// </summary>
        public static void RemoveTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return;

            var tables = GetTrackedTables();
            tables.RemoveAll(t => string.Equals(t, tableName, StringComparison.OrdinalIgnoreCase));
            SaveTrackedTables(tables);
        }

        /// <summary>
        /// Clears all tracked tables.
        /// </summary>
        public static void ClearAll()
        {
            SaveTrackedTables(new List<string>());
        }

        /// <summary>
        /// Gets the full path to the tablelist.json configuration file.
        /// </summary>
        public static string GetConfigFilePath()
        {
            return GetConfigPath();
        }
    }
}
