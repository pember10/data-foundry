namespace WTW.Diffusion.Core.Abstractions
{
    /// <summary>
    /// Abstraction for configuration/settings access.
    /// Implementations can target Visual Studio settings, config files, environment variables, etc.
    /// </summary>
    public interface IConfigurationProvider
    {
        /// <summary>
        /// Gets the connection string for the local/target database.
        /// </summary>
        string LocalDatabaseConnection { get; }

        /// <summary>
        /// Gets the connection string for the shadow database (optional).
        /// </summary>
        string ShadowDatabaseConnection { get; }

        /// <summary>
        /// Gets the name of the SQL project.
        /// </summary>
        string SqlProject { get; }

        /// <summary>
        /// Gets the migrations folder path relative to the SQL project.
        /// </summary>
        string MigrationsFolder { get; }

        /// <summary>
        /// Gets a value indicating whether to use PowerShell script execution mode.
        /// </summary>
        bool UsePowerShellScript { get; }

        /// <summary>
        /// Gets the path to the PowerShell script (if using PowerShell mode).
        /// </summary>
        string PowerShellScriptPath { get; }

        /// <summary>
        /// Gets the list of tracked tables for change detection.
        /// </summary>
        string[] TrackedTables { get; }
    }
}
