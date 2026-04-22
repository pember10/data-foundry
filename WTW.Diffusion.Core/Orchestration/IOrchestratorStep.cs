using System;
using System.Threading.Tasks;
using WTW.Diffusion.Core.Abstractions;

namespace WTW.Diffusion.Core.Orchestration
{
    /// <summary>
    /// Represents a discrete, composable step in a migration orchestration pipeline.
    /// Designed to support future visual orchestration tooling (e.g., a drag-and-drop designer).
    /// </summary>
    public interface IOrchestratorStep
    {
        /// <summary>
        /// The display name of this step shown in a designer or log.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Executes the step against the provided context.
        /// </summary>
        Task ExecuteAsync(OrchestratorContext context);
    }

    /// <summary>
    /// Shared state passed between orchestration steps.
    /// </summary>
    public class OrchestratorContext
    {
        /// <summary>The target database name.</summary>
        public string TargetDatabase { get; set; }

        /// <summary>The shadow database name.</summary>
        public string ShadowDatabase { get; set; }

        /// <summary>The target SQL Server instance.</summary>
        public string TargetServer { get; set; }

        /// <summary>Root path containing migration scripts.</summary>
        public string MigrationsPath { get; set; }

        /// <summary>Root path for generated migration output scripts.</summary>
        public string OutputMigrationDir { get; set; }

        /// <summary>Logger for step output.</summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Arbitrary step-to-step state bag for passing outputs forward in the pipeline.
        /// Key: step name or well-known constant. Value: step output.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> State { get; }
            = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}
