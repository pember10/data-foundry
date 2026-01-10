using System.Collections.Generic;

namespace data_foundry.Services
{
    /// <summary>
    /// Result of PowerShell script execution.
    /// </summary>
    public class PowerShellResult
    {
        /// <summary>
        /// Whether the script executed successfully (no errors).
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Output lines from the script.
        /// </summary>
        public List<string> Output { get; set; }

        /// <summary>
        /// Error messages from the script.
        /// </summary>
        public List<string> Errors { get; set; }

        public PowerShellResult()
        {
            Output = new List<string>();
            Errors = new List<string>();
        }
    }
}
