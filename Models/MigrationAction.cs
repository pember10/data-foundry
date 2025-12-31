namespace data_foundry.Models
{
    /// <summary>
    /// Actions that can be taken when data changes are detected.
    /// </summary>
    public enum MigrationAction
    {
        /// <summary>
        /// Revert changes in the target database to match the shadow database.
        /// </summary>
        Revert,

        /// <summary>
        /// Generate a migration script from the detected changes.
        /// </summary>
        Migrate,

        /// <summary>
        /// Cancel/exit without taking action.
        /// </summary>
        Cancel
    }
}
