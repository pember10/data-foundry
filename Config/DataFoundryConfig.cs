using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using data_foundry.Helpers;
using static data_foundry.Constants;

namespace data_foundry.Config
{
    public static class DataFoundryConfig
    {
        public static TableListConfig LoadTableList()
        {
            try
            {
                // Get the extension's install directory
                var installDir = PathHelper.GetExtensionInstallDirectory(typeof(TableListConfig));
                var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
                
                if (string.IsNullOrEmpty(configDir))
                {
                    return new TableListConfig { Tables = new List<string>() };
                }

                var configPath = PathHelper.SafeCombine(configDir, "tablelist.json");
                
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                    return new TableListConfig { Tables = new List<string>() };

                var json = File.ReadAllText(configPath);
                return JsonConvert.DeserializeObject<TableListConfig>(json);
            }
            catch
            {
                return new TableListConfig { Tables = new List<string>() };
            }
        }
    }
}
