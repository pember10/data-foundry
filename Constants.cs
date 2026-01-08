using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace data_foundry
{
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
    }
}