using System.Collections.Generic;

namespace data_foundry.Models
{
    /// <summary>
    /// Configuration for SQL migration automation including tracked tables.
    /// </summary>
    public class MigrationConfig
    {
        /// <summary>
        /// List of tables to track for data changes between target and shadow databases.
        /// </summary>
        public List<string> TrackedTables { get; set; }

        public MigrationConfig()
        {
            TrackedTables = new List<string>();
        }
    }
}
