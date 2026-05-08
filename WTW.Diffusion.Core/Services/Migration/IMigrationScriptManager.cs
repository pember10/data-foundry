using System;
using System.Collections.Generic;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Core.Services.Migration
{
    public interface IMigrationScriptManager
    {
        MigrationInfo GetMigrationInfoFromFile(string path);
        List<MigrationInfo> GetPendingMigrations(string database);
        string GetRelativeFilename(string fullPath);
        void ExecuteMigrationScript(string database, MigrationInfo migrationInfo, bool skipExecution = false);
    }
}
