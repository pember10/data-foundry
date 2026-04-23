using System.CommandLine;
using Newtonsoft.Json;
using WTW.Diffusion.Cli.Adapters;

namespace WTW.Diffusion.Cli.Commands;

/// <summary>
/// <c>init</c> — scaffolds a starter <c>config.json</c> in the current (or specified) directory.
/// The format matches the ConfigPath expected by SqlMetadataAutomation.ps1.
/// </summary>
internal static class InitCommand
{
    internal static Command Build()
    {
        var outputOption = new Option<string>(
            ["--output", "-o"],
            description: "Directory where config.json will be created.",
            getDefaultValue: () => Directory.GetCurrentDirectory());

        var cmd = new Command("init",
            "Create a default config.json file (TrackedTables configuration).")
        {
            outputOption
        };

        cmd.SetHandler((outputDir) =>
        {
            var logger = new ConsoleLogger();
            string dest = Path.Combine(outputDir, "config.json");

            if (File.Exists(dest))
            {
                logger.LogWarning($"config.json already exists: {dest}");
                return;
            }

            var defaultConfig = new { TrackedTables = new[] { "dbo.MyTable" } };

            Directory.CreateDirectory(outputDir);
            File.WriteAllText(dest, JsonConvert.SerializeObject(defaultConfig, Formatting.Indented));

            logger.Log($"Created {dest}");
            logger.Log("Edit TrackedTables, then invoke wtw-diffusion with --config-path pointing to this file.");
        }, outputOption);

        return cmd;
    }
}
