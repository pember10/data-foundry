using System.Collections.Generic;

namespace WTW.Diffusion.Core.Services.Migration
{
    /// <summary>
    /// Result of PowerShell script execution.
    /// </summary>
    public class PowerShellResult
    {
        public bool Success { get; set; }
        public List<string> Output { get; set; }
        public List<string> Errors { get; set; }

        public PowerShellResult()
        {
            Output = new List<string>();
            Errors = new List<string>();
        }
    }
}
