using data_foundry.Options;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;

namespace data_foundry.Services
{
    /// <summary>
    /// Factory for creating SqlMigrationOrchestrator instances from Visual Studio package options.
    /// </summary>
    public static class SqlMigrationOrchestratorFactory
    {
        /// <summary>
        /// Creates a SqlMigrationOrchestrator using the current package options.
        /// </summary>
        /// <param name="package">The VS package instance.</param>
        /// <returns>Configured SqlMigrationOrchestrator instance.</returns>
        public static SqlMigrationOrchestrator Create(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (package == null)
                throw new ArgumentNullException(nameof(package));

            // Get options from the package
            if (!(package.GetDialogPage(typeof(DataFoundryOptions)) is DataFoundryOptions options))
                throw new InvalidOperationException("Unable to retrieve options from package.");

            // Get DTE service
            if (!(Package.GetGlobalService(typeof(DTE)) is DTE environment))
                throw new InvalidOperationException("Unable to retrieve Development Tool Environment service.");

            return new SqlMigrationOrchestrator(options, environment);
        }

        /// <summary>
        /// Creates a SqlMigrationOrchestrator using the data-foundry package singleton.
        /// </summary>
        /// <returns>Configured SqlMigrationOrchestrator instance.</returns>
        public static SqlMigrationOrchestrator CreateFromGlobalPackage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var package = data_foundryPackage.Instance;
            return package != null 
                ? Create(package) 
                : throw new InvalidOperationException("Data Foundry package is not initialized.");
        }
    }
}
