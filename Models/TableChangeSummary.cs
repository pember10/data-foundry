namespace data_foundry.Models
{
    /// <summary>
    /// Summary of data changes detected for a specific table.
    /// </summary>
    public class TableChangeSummary
    {
        /// <summary>
        /// Name of the table.
        /// </summary>
        public string Table { get; set; }

        /// <summary>
        /// Number of rows inserted (present in target, absent in shadow).
        /// </summary>
        public int Inserts { get; set; }

        /// <summary>
        /// Number of rows updated (PK exists in both, non-PK columns differ).
        /// </summary>
        public int Updates { get; set; }

        /// <summary>
        /// Number of rows deleted (present in shadow, absent in target).
        /// </summary>
        public int Deletes { get; set; }

        /// <summary>
        /// Indicates whether any changes were detected.
        /// </summary>
        public bool HasChanges => Inserts > 0 || Updates > 0 || Deletes > 0;
    }
}
