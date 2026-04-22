using data_foundry.Options;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Config;

namespace data_foundry.Services.Adapters
{
    /// <summary>
    /// IConfigurationProvider adapter for the Visual Studio host.
    /// Reads settings from the DataFoundryOptions dialog page.
    /// </summary>
    public class VsConfigurationProvider : IConfigurationProvider
    {
        private readonly DataFoundryOptions _options;

        public VsConfigurationProvider(DataFoundryOptions options)
        {
            _options = options;
        }

        public string LocalDatabaseConnection => _options.LocalDatabaseConnection;

        public string ShadowDatabaseConnection => _options.ShadowDatabaseConnection;

        public string SqlProject => _options.SqlProject;

        public string MigrationsFolder => _options.MigrationsFolder;

        public bool UsePowerShellScript => _options.UsePowerShellScript;

        public string PowerShellScriptPath => null; // Resolved at runtime via PathHelper in the VSIX

        public string[] TrackedTables
        {
            get
            {
                var config = DataFoundryConfig.LoadTableList();
                return config.Tables?.ToArray() ?? new string[0];
            }
        }
    }
}
