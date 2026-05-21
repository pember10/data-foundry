using System.CommandLine;
using WTW.Diffusion.Cli.Commands;

var root = new RootCommand("WTW Diffusion CLI — data migration tool for SQL Server Data Projects.");

root.Add(DeployCommand.Build());
root.Add(DetectChangesCommand.Build());
root.Add(GenerateScriptCommand.Build());
root.Add(InitCommand.Build());

return await root.InvokeAsync(args);

// Exit codes:
//   0 = success
//   1 = cancelled by user (OperationCanceledException)
//   2 = unhandled error
//   3 = .sqlproj not found (--sql-project specified but project could not be located)

