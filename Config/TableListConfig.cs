using System.Collections.Generic;
using Newtonsoft.Json;

namespace data_foundry.Config
{
    public class TableListConfig
    {
        [JsonProperty("table")]
        public List<string> Tables { get; set; }
    }
}
