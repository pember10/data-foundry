using System.Collections.Generic;
using Newtonsoft.Json;

namespace WTW.Diffusion.Core.Config
{
    public class TableListConfig
    {
        [JsonProperty("tables")]
        public List<string> Tables { get; set; }
    }
}
