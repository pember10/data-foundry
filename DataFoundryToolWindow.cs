using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace data_foundry
{
    [Guid("b2e7a1c2-2e4a-4e7a-9c2a-1e2a3c4b5d6f")]
    public class DataFoundryToolWindow : ToolWindowPane
    {
        public DataFoundryToolWindow() : base(null)
        {
            this.Caption = "WTW Diffusion";
            this.Content = new DataFoundryToolWindowControl();
        }
    }
}
