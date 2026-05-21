using System;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Services.Database;

namespace WTW.Diffusion.Core.Helpers
{
    /// <summary>
    /// Reads the Database Schema Provider (DSP) from a .sqlproj file and uses it to
    /// derive the minimum SQL Server version the project targets.
    /// </summary>
    public static class SqlProjectDspReader
    {
        // Matches e.g. "Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider"
        private static readonly Regex DspVersionRegex =
            new Regex(@"Sql(\d+)DatabaseSchemaProvider", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads the &lt;DSP&gt; element from the first &lt;PropertyGroup&gt; in a .sqlproj file.
        /// </summary>
        /// <param name="projectFilePath">Full path to the .sqlproj file.</param>
        /// <returns>The DSP value, or <c>null</c> if the file is missing, unreadable, or has no DSP element.</returns>
        public static string ReadDsp(string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath))
                return null;

            try
            {
                var doc = XDocument.Load(projectFilePath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                return doc.Root?.Element(ns + "PropertyGroup")?.Element(ns + "DSP")?.Value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parses a DSP string into the minimum SQL Server major version it implies.
        /// </summary>
        /// <param name="dsp">The full DSP string, e.g. "Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider".</param>
        /// <returns>
        /// The major version integer (e.g. 15), or <c>null</c> for Azure SQL, unknown, or unset DSP values.
        /// </returns>
        public static int? ParseMinimumMajorVersion(string dsp)
        {
            if (string.IsNullOrWhiteSpace(dsp))
                return null;

            // Azure SQL has no comparable on-premises version — skip the check
            if (dsp.IndexOf("Azure", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            var match = DspVersionRegex.Match(dsp);
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups[1].Value, out int raw))
                return null;

            // All SSDT DSP suffixes encode as version × 10: Sql90→9, Sql100→10, Sql150→15, Sql160→16
            return raw / 10;
        }

        /// <summary>
        /// Validates the connected SQL Server version against the project's minimum requirement and
        /// logs a warning when the server does not meet it. Advisory only — never throws.
        /// </summary>
        /// <param name="targetDatabase">Name of the target database (used to query server version).</param>
        /// <param name="repository">Repository used to query <c>SERVERPROPERTY('ProductMajorVersion')</c>.</param>
        /// <param name="logger">Logger for info and warning messages.</param>
        /// <param name="minVersionOverride">
        /// Override for the minimum version:
        /// <list type="bullet">
        ///   <item><c>null</c> — auto-detect from the .sqlproj DSP via <paramref name="projectFilePath"/></item>
        ///   <item><c>0</c> — skip the check entirely</item>
        ///   <item>positive int — enforce this version as the minimum (ignores DSP)</item>
        /// </list>
        /// </param>
        /// <param name="projectFilePath">Full path to the .sqlproj file (used when <paramref name="minVersionOverride"/> is <c>null</c>).</param>
        public static void ValidateAndWarn(
            string targetDatabase,
            ISqlMigrationRepository repository,
            ILogger logger,
            int? minVersionOverride,
            string projectFilePath)
        {
            if (minVersionOverride == 0)
                return;

            int? effectiveMin;

            if (minVersionOverride.HasValue)
            {
                effectiveMin = minVersionOverride;
            }
            else
            {
                var dsp = ReadDsp(projectFilePath);
                effectiveMin = ParseMinimumMajorVersion(dsp);
            }

            if (!effectiveMin.HasValue)
                return;

            try
            {
                int actual = repository.GetServerMajorVersion(targetDatabase);

                if (actual >= effectiveMin.Value)
                {
                    logger.Log(
                        $"Server version check passed: SQL Server {actual} meets the " +
                        $"project minimum of {effectiveMin.Value}.");
                }
                else
                {
                    logger.LogWarning(
                        $"SQL Server {actual} may not support all features required by this project " +
                        $"(minimum: SQL Server {effectiveMin.Value}). " +
                        $"Review the <DSP> element in the .sqlproj or set a --min-sql-version override.");
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — server version check should never block normal operation
                logger.LogWarning($"Could not determine SQL Server version: {ex.Message}");
            }
        }
    }
}
