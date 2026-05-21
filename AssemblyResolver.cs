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

                switch (assemblyName.Name)
                {
                    case "Azure.Core":
                        return LoadFromExtensionDirectory("Azure.Core.dll");
                    case "Azure.Identity":
                        return LoadFromExtensionDirectory("Azure.Identity.dll");
                    case "Microsoft.Identity.Client":
                        return LoadFromExtensionDirectory("Microsoft.Identity.Client.dll");
                    case "System.Memory":
                        return LoadFromExtensionDirectory("System.Memory.dll");
                    case "System.Text.Json":
                        return LoadFromExtensionDirectory("System.Text.Json.dll");
                    case "System.Threading.Tasks.Extensions":
                        return LoadFromExtensionDirectory("System.Threading.Tasks.Extensions.dll");
                    case "System.Runtime.CompilerServices.Unsafe":
                        return LoadFromExtensionDirectory("System.Runtime.CompilerServices.Unsafe.dll");
                    case "System.Buffers":
                        return LoadFromExtensionDirectory("System.Buffers.dll");
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
            // 1. Extension install directory — normal case
            var extensionDir = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
            var assemblyPath = Path.Combine(extensionDir, assemblyFileName);
            if (File.Exists(assemblyPath))
                return Assembly.LoadFrom(assemblyPath);

            // 2. VS IDE directories — for assemblies VSSDK excludes from VSIX but VS ships itself
            //    AppDomain.CurrentDomain.BaseDirectory is VS's Common7\IDE\ when running inside VS.
            var ideDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var subDir in new[] { string.Empty, "PublicAssemblies", "PrivateAssemblies" })
            {
                assemblyPath = string.IsNullOrEmpty(subDir)
                    ? Path.Combine(ideDir, assemblyFileName)
                    : Path.Combine(ideDir, subDir, assemblyFileName);

                if (File.Exists(assemblyPath))
                    return Assembly.LoadFrom(assemblyPath);
            }

            return null;
        }
    }
}
