using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace data_foundry
{
    internal sealed class ShowDataFoundryToolWindowCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("c1e7a1c2-2e4a-4e7a-9c2a-1e2a3c4b5d6f");
        private readonly AsyncPackage package;

        private ShowDataFoundryToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package;
            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            new ShowDataFoundryToolWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            package.JoinableTaskFactory.RunAsync(async delegate
            {
                ToolWindowPane window = await package.ShowToolWindowAsync(
                    typeof(DataFoundryToolWindow),
                    0,
                    true,
                    package.DisposalToken);
            });
        }
    }
}
