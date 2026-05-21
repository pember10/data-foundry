using Newtonsoft.Json;

namespace WTW.Diffusion.Cli.Infrastructure;

internal sealed class TrackedTablesConfig
{
    [JsonProperty("TrackedTables")]
    public string[] TrackedTables { get; set; } = [];
}
