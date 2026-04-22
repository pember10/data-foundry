using System;
using System.IO;
using System.Reflection;

namespace data_foundry
{
    /// <summary>
    /// Handles assembly resolution for dependencies that may have version mismatches.
    /// Required because VSIX app.config binding redirects don't always work in VS.
    /// </summary>
    internal static class AssemblyResolver
    {
        private static bool _isInitialized;

        /// <summary>
        /// Initializes the assembly resolver. Call this early in package initialization.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _isInitialized = true;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var assemblyName = new AssemblyName(args.Name);
                
                // Handle Azure.Core version redirects
                if (assemblyName.Name == "Azure.Core")
                {
                    return LoadFromExtensionDirectory("Azure.Core.dll");
                }

                // Handle System.Memory version redirects
                if (assemblyName.Name == "System.Memory")
                {
                    return LoadFromExtensionDirectory("System.Memory.dll");
                }

                // Handle System.Text.Json version redirects
                if (assemblyName.Name == "System.Text.Json")
                {
                    return LoadFromExtensionDirectory("System.Text.Json.dll");
                }

                // Handle Azure.Identity version redirects
                if (assemblyName.Name == "Azure.Identity")
                {
                    return LoadFromExtensionDirectory("Azure.Identity.dll");
                }

                // Handle Azure.Identity version redirects
                if (assemblyName.Name == "Azure.Identity")
                {
                    return LoadFromExtensionDirectory("Azure.Identity.dll");
                }

                // Handle Microsoft.Identity.Client version redirects
                if (assemblyName.Name == "Microsoft.Identity.Client")
                {
                    return LoadFromExtensionDirectory("Microsoft.Identity.Client.dll");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AssemblyResolver error: {ex.Message}");
            }

            return null;
        }

        private static Assembly LoadFromExtensionDirectory(string assemblyFileName)
        {
            var extensionDir = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
            var assemblyPath = Path.Combine(extensionDir, assemblyFileName);

            if (File.Exists(assemblyPath))
            {
                return Assembly.LoadFrom(assemblyPath);
            }

            return null;
        }
    }
}
