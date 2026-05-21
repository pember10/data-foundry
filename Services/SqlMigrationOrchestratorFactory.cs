using data_foundry.Options;
using data_foundry.Services.Adapters;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Services;

namespace data_foundry.Services
{
    /// <summary>
    /// Factory for creating SqlMigrationOrchestrator instances from Visual Studio package options.
    /// </summary>
    public static class SqlMigrationOrchestratorFactory
    {
        /// <summary>
        /// Creates a SqlMigrationOrchestrator using the current package options.
        /// Path defaults for migrationLogSchema and shadowCache are resolved inside the Core orchestrator.
        /// </summary>
        public static SqlMigrationOrchestrator Create(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (package == null)
                throw new ArgumentNullException(nameof(package));

            var (logger, configuration, projectManager) = CreateAdapters(package);
            return new SqlMigrationOrchestrator(logger, configuration, projectManager);
        }

        /// <summary>
        /// Creates a SqlMigrationOrchestrator using the data-foundry package singleton.
        /// </summary>
        public static SqlMigrationOrchestrator CreateFromGlobalPackage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var package = data_foundryPackage.Instance;
            return package != null
                ? Create(package)
                : throw new InvalidOperationException("Data Foundry package is not initialized.");
        }

        /// <summary>
        /// Creates the three VS adapter implementations of the Core abstractions.
        /// </summary>
        public static (ILogger Logger, IConfigurationProvider Configuration, IProjectManager ProjectManager)
            CreateAdapters(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (package == null)
                throw new ArgumentNullException(nameof(package));

            if (!(package.GetDialogPage(typeof(DataFoundryOptions)) is DataFoundryOptions options))
                throw new InvalidOperationException("Unable to retrieve options from package.");

            if (!(Package.GetGlobalService(typeof(DTE)) is DTE environment))
                throw new InvalidOperationException("Unable to retrieve Development Tool Environment service.");

            return (
                new VsLogger(options),
                new VsConfigurationProvider(options),
                new VsProjectManager(environment)
            );
        }
    }
}
