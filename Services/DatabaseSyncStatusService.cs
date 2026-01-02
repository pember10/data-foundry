using System;

namespace data_foundry.Services
{
    /// <summary>
    /// Service to notify all tabs when database sync status changes.
    /// </summary>
    public class DatabaseSyncStatusService
    {
        private static readonly Lazy<DatabaseSyncStatusService> _instance = 
            new Lazy<DatabaseSyncStatusService>(() => new DatabaseSyncStatusService());

        public static DatabaseSyncStatusService Instance => _instance.Value;

        /// <summary>
        /// Event raised when database sync status changes (migrations applied, database changed, etc.)
        /// </summary>
        public event EventHandler SyncStatusChanged;

        private DatabaseSyncStatusService()
        {
        }

        /// <summary>
        /// Notify all subscribers that sync status has changed and they should re-check.
        /// </summary>
        public void NotifySyncStatusChanged()
        {
            SyncStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
