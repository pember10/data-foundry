using System;
using System.IO;
using System.Linq;

namespace WTW.Diffusion.Core.Helpers
{
    /// <summary>
    /// Provides utility methods for path sanitization and validation.
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Sanitizes a path component (file or directory name) by removing invalid characters.
        /// </summary>
        /// <param name="pathComponent">The path component to sanitize.</param>
        /// <param name="replacement">Character to replace invalid characters with (default: underscore).</param>
        /// <returns>Sanitized path component.</returns>
        public static string SanitizePathComponent(string pathComponent, char replacement = '_')
        {
            if (string.IsNullOrWhiteSpace(pathComponent))
                return pathComponent;

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = pathComponent;
            
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, replacement);
            }

            return sanitized;
        }

        /// <summary>
        /// Validates a full path to ensure it doesn't contain invalid characters.
        /// </summary>
        /// <param name="path">The path to validate.</param>
        /// <returns>True if the path is valid, false otherwise.</returns>
        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Try to get the full path - this will throw if the path is invalid
                var fullPath = Path.GetFullPath(path);
                
                // Additional check for invalid characters in path components
                var invalidPathChars = Path.GetInvalidPathChars();
                return !path.Any(c => invalidPathChars.Contains(c));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely gets the directory name from an assembly location.
        /// Handles cases where the assembly path might be invalid or in a shadow copy location.
        /// </summary>
        /// <param name="assemblyLocation">The assembly location path.</param>
        /// <returns>The directory name, or null if invalid.</returns>
        public static string GetSafeDirectoryName(string assemblyLocation)
        {
            if (string.IsNullOrWhiteSpace(assemblyLocation))
                return null;

            try
            {
                // Handle vshost scenarios and shadow copy paths
                var location = assemblyLocation;
                
                // Normalize the path
                if (IsValidPath(location))
                {
                    return Path.GetDirectoryName(Path.GetFullPath(location));
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Safely combines path components, validating each one.
        /// </summary>
        /// <param name="paths">Path components to combine.</param>
        /// <returns>Combined path if valid, null otherwise.</returns>
        public static string SafeCombine(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return null;

            try
            {
                // Filter out null or empty paths
                var validPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                
                if (validPaths.Length == 0)
                    return null;

                var combined = Path.Combine(validPaths);
                
                // Validate the combined path
                return IsValidPath(combined) ? combined : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the extension installation directory with fallback to temp directory.
        /// </summary>
        /// <param name="assemblyType">Type from the assembly to locate.</param>
        /// <returns>Installation directory path.</returns>
        public static string GetExtensionInstallDirectory(Type assemblyType)
        {
            try
            {
                var assemblyLocation = assemblyType.Assembly.Location;
                var installDir = GetSafeDirectoryName(assemblyLocation);

                if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                    return installDir;

                // Fallback: use a dedicated folder in user's AppData
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var fallbackDir = Path.Combine(appDataPath, "WTW.Diffusion", "Config");
                
                if (!Directory.Exists(fallbackDir))
                    Directory.CreateDirectory(fallbackDir);

                return fallbackDir;
            }
            catch
            {
                // Last resort: use temp directory
                var tempPath = Path.GetTempPath();
                var fallbackDir = Path.Combine(tempPath, "WTW.Diffusion", "Config");
                
                if (!Directory.Exists(fallbackDir))
                    Directory.CreateDirectory(fallbackDir);

                return fallbackDir;
            }
        }

        /// <summary>
        /// Ensures a directory exists, creating it if necessary.
        /// </summary>
        /// <param name="directoryPath">The directory path.</param>
        /// <returns>True if the directory exists or was created, false otherwise.</returns>
        public static bool EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
