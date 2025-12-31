using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace data_foundry.Config
{
    public static class DataFoundryConfig
    {
        public static TableListConfig LoadTableList()
        {
            // Get the extension's install directory
            var assemblyPath = typeof(TableListConfig).Assembly.Location;
            var installDir = Path.GetDirectoryName(assemblyPath);
            var configPath = Path.Combine(installDir, "tablelist.json");

            if (!File.Exists(configPath))
                return new TableListConfig { Tables = new List<string>() };

            var json = File.ReadAllText(configPath);
            return JsonConvert.DeserializeObject<TableListConfig>(json);
        }
    }
}
