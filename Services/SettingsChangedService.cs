using System;

namespace data_foundry.Services
{
    /// <summary>
    /// Service to notify all tabs when settings are changed.
    /// </summary>
    public class SettingsChangedService
    {
        private static readonly Lazy<SettingsChangedService> _instance = 
            new Lazy<SettingsChangedService>(() => new SettingsChangedService());

        public static SettingsChangedService Instance => _instance.Value;

        /// <summary>
        /// Event raised when settings are changed (connection strings, project, etc.)
        /// </summary>
        public event EventHandler SettingsChanged;

        private SettingsChangedService()
        {
        }

        /// <summary>
        /// Notify all subscribers that settings have changed and they should refresh.
        /// </summary>
        public void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
