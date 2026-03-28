using Newtonsoft.Json;
using System;

namespace WTW.Diffusion.Core.Models
{
    /// <summary>
    /// Cached information about the shadow database state.
    /// </summary>
    public class ShadowDatabaseCacheInfo
    {
        /// <summary>
        /// Hash of all migration files (filename + timestamp).
        /// </summary>
        [JsonProperty("hash")]
        public string Hash { get; set; }

        /// <summary>
        /// Number of migrations applied to shadow database.
        /// </summary>
        [JsonProperty("migrationCount")]
        public int MigrationCount { get; set; }

        /// <summary>
        /// When the shadow database was last synchronized.
        /// </summary>
        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Name of the shadow database.
        /// </summary>
        [JsonProperty("databaseName")]
        public string DatabaseName { get; set; }
    }
}
