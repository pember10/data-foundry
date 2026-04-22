namespace WTW.Diffusion.Core
{
    /// <summary>
    /// Global constants used throughout the WTW Diffusion system.
    /// </summary>
    public static class Constants
    {
        public static class FileExtensions
        {
            public const string Json = ".json";
            public const string Sql = ".sql";
            public const string SqlProj = ".sqlproj";
        }

        public static class Folders
        {
            public const string Config = "Config";
            public const string Migrations = "Migrations";
            public const string Scripts = "Scripts";
        }

        public static class StaticFiles
        {
            public const string TableListJson = "tablelist.json";
            public const string SqlMetadataAutomationPs1 = "SqlMetadataAutomation.ps1";
        }

        public static class Tables
        {
            public const string MigrationLog = "__MigrationLog";
        }

        public static class Azure
        {
            public const string DefaultTokenScope = "https://database.windows.net/.default";
            public const string AzureSqlDomain = "database.windows.net";
        }

        public static class RegularExpressions
        {
            public const string SqlIdentifier = @"^[a-zA-Z_][a-zA-Z0-9_]*$";
            public const string MigrationIdPattern = @"--\s*<Migration\s+ID=""(?<id>[0-9a-fA-F-]{36})""\s*/>";
        }

        public static class Parameters
        {
            public const string TargetDatabase = "TargetDatabase";
            public const string TargetServer = "TargetServer";
            public const string MigrationsPath = "MigrationsPath";
            public const string ConfirmTargetMigration = "ConfirmTargetMigration";
            public const string DetectChanges = "DetectChanges";
            public const string ConfigPath = "ConfigPath";
            public const string Action = "Action";
            public const string ScriptName = "ScriptName";

        }

        public static class Actions
        {
            public const string Apply = "Apply";
            public const string Migrate = "Migrate";
            public const string Revert = "Revert";
        }
    }
}
