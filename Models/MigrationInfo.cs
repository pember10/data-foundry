using System;

namespace data_foundry.Models
{
    /// <summary>
    /// Represents information about a migration script file.
    /// </summary>
    public class MigrationInfo
    {
        /// <summary>
        /// Unique identifier for the migration (GUID).
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Full SQL content of the migration script.
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// File name only (without path).
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Full path to the migration script file.
        /// </summary>
        public string FullPath { get; set; }
    }
}
