# WTW Diffusion - Architecture Separation Plan

## ?? Overview

**Date:** January 30, 2025  
**Status:** In Progress  
**Goal:** Separate VSIX-specific code from reusable business logic to enable Azure DevOps pipeline integration

---

## ?? Target Architecture

```
Solution: WTW.Diffusion.sln
?
??? WTW.Diffusion.Core (NEW)
?   ??? Target Framework: .NET Standard 2.0
?   ??? Purpose: Platform-independent business logic
?   ??? Dependencies: Microsoft.Data.SqlClient, Newtonsoft.Json
?   ??? Contents:
?       ??? Models/ (all existing models)
?       ??? Services/
?       ?   ??? Database/
?       ?   ?   ??? SqlMigrationRepository.cs
?       ?   ?   ??? ChangeDetectionService.cs
?       ?   ?   ??? AzureSqlAuthenticationProvider.cs
?       ?   ??? Migration/
?       ?   ?   ??? MigrationScriptManager.cs
?       ?   ?   ??? MigrationScriptGenerator.cs
?       ?   ?   ??? IMigrationExecutor.cs
?       ?   ?   ??? CSharpMigrationExecutor.cs
?       ?   ?   ??? PowerShellMigrationExecutor.cs
?       ?   ?   ??? PowerShellScriptRunner.cs
?       ?   ?   ??? PowerShellOutputParser.cs
?       ?   ??? Storage/
?       ?       ??? ActivityHistoryService.cs
?       ?       ??? ChangeDetectionResultsService.cs
?       ?       ??? TrackedTablesManager.cs
?       ??? Config/ (all config classes)
?       ??? Helpers/ (PathHelper, etc.)
?       ??? Abstractions/
?       ?   ??? ILogger.cs (new)
?       ?   ??? IProjectManager.cs (new)
?       ?   ??? IConfigurationProvider.cs (new)
?       ??? Constants.cs
?
??? WTW.Diffusion.VisualStudio (RENAMED from data-foundry)
?   ??? Target Framework: .NET Framework 4.7.2
?   ??? Purpose: Visual Studio extension (VSIX)
?   ??? Dependencies: 
?       ??? Project reference to WTW.Diffusion.Core
?       ??? Microsoft.VisualStudio.SDK
?       ??? Microsoft.VSSDK.BuildTools
?   ??? Contents:
?       ??? Views/ (all XAML files)
?       ??? Options/ (DataFoundryOptions.cs)
?       ??? Adapters/
?       ?   ??? VsLogger.cs (implements ILogger)
?       ?   ??? VsProjectManager.cs (wraps ProjectFileManager)
?       ?   ??? VsConfigurationProvider.cs (implements IConfigurationProvider)
?       ??? Services/
?       ?   ??? OutputWindowLogger.cs
?       ?   ??? GlobalProcessingStateService.cs
?       ?   ??? DatabaseSyncStatusService.cs
?       ?   ??? SettingsChangedService.cs
?       ?   ??? ServicesHelper.cs
?       ?   ??? ProjectFileManager.cs (stays VS-specific)
?       ??? Converters/ (XAML converters)
?       ??? DataFoundryToolWindow.cs
?       ??? DataFoundryToolWindowControl.xaml(.cs)
?       ??? ShowDataFoundryToolWindowCommand.cs
?       ??? data_foundryPackage.cs
?       ??? AssemblyResolver.cs
?
??? WTW.Diffusion.Cli (NEW)
    ??? Target Framework: .NET Core 3.1
    ??? Purpose: Command-line tool for Azure DevOps pipelines
    ??? Dependencies:
    ?   ??? Project reference to WTW.Diffusion.Core
    ?   ??? System.CommandLine (for CLI parsing)
    ??? Contents:
        ??? Program.cs (entry point)
        ??? Commands/
        ?   ??? DetectChangesCommand.cs
        ?   ??? GenerateScriptCommand.cs
        ?   ??? DeployCommand.cs
        ??? Adapters/
        ?   ??? ConsoleLogger.cs (implements ILogger)
        ?   ??? FileConfigurationProvider.cs (implements IConfigurationProvider)
        ??? README.md (usage documentation)
```

---

## ?? Migration Steps

### Phase 1: Create Core Library ?
1. ? Create `WTW.Diffusion.Core` project (.NET Standard 2.0)
2. ? Add NuGet packages
3. ? Create folder structure
4. ? Define abstractions (ILogger, IProjectManager, IConfigurationProvider)

### Phase 2: Move Models & Simple Classes ?
1. ? Move all files from `Models/` folder
2. ? Move `Constants.cs`
3. ? Move `Config/` folder classes
4. ? Update namespaces to `WTW.Diffusion.Core`
5. ? Move `PathHelper.cs`
6. ? Build verification successful

### Phase 3: Move Database Services ??
1. ? Move `SqlMigrationRepository.cs`
2. ? Move `ChangeDetectionService.cs`
3. ? Move `AzureSqlAuthenticationProvider.cs`
4. ? Update dependencies to use abstractions

### Phase 4: Move Migration Services ?

### Phase 5: Move Storage Services ?
1. ? Move `ActivityHistoryService.cs`
2. ? Move `ChangeDetectionResultsService.cs`
3. ? Move `TrackedTablesManager.cs`

### Phase 6: Move Helpers ?
1. ? Move `PathHelper.cs`
2. ? Update any VS-specific path logic

### Phase 7: Rename & Update VSIX Project
1. Rename `data-foundry.csproj` ? `WTW.Diffusion.VisualStudio.csproj`
2. Add project reference to `WTW.Diffusion.Core`
3. Update all `using` statements
4. Create adapter implementations
5. Update `SqlMigrationOrchestratorFactory` to use abstractions

### Phase 8: Create CLI Project
1. Create `WTW.Diffusion.Cli` project (.NET Core 3.1)
2. Add project reference to `WTW.Diffusion.Core`
3. Implement command-line interface
4. Create adapter implementations
5. Write documentation

### Phase 9: Testing & Validation
1. Build all projects
2. Test VSIX in experimental instance
3. Test CLI tool locally
4. Update all documentation
5. Update README.md

---

## ?? NuGet Package Requirements

### WTW.Diffusion.Core
```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.0-preview3.25342.7" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="Microsoft.Identity.Client" Version="4.78.0" />
<PackageReference Include="System.Management.Automation" Version="5.1.1" />
```

### WTW.Diffusion.VisualStudio
```xml
<ProjectReference Include="..\WTW.Diffusion.Core\WTW.Diffusion.Core.csproj" />
<PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.0.32112.339" />
<PackageReference Include="Microsoft.VSSDK.BuildTools" Version="17.14.2120" />
```

### WTW.Diffusion.Cli
```xml
<ProjectReference Include="..\WTW.Diffusion.Core\WTW.Diffusion.Core.csproj" />
<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="6.0.0" />
```

---

## ??? Abstraction Interfaces

### ILogger
```csharp
namespace WTW.Diffusion.Core.Abstractions
{
    public interface ILogger
    {
        void Log(string message);
        void LogError(string message);
        void LogWarning(string message);
        void LogDebug(string message);
    }
}
```

### IProjectManager
```csharp
namespace WTW.Diffusion.Core.Abstractions
{
    public interface IProjectManager
    {
        bool AddFileToProject(string projectName, string filePath, string folderPath = null);
        string GetProjectPath(string projectName);
        string GetRelativeFolderPath(string projectPath, string filePath);
    }
}
```

### IConfigurationProvider
```csharp
namespace WTW.Diffusion.Core.Abstractions
{
    public interface IConfigurationProvider
    {
        string LocalDatabaseConnection { get; }
        string ShadowDatabaseConnection { get; }
        string SqlProject { get; }
        string MigrationsFolder { get; }
        bool UsePowerShellScript { get; }
    }
}
```

---

## ?? Breaking Changes & Migration Guide

### For VSIX Users
- **No breaking changes** - Extension will continue to work identically
- Internal architecture changed but public API remains the same

### For Future CLI Users
- New command-line tool available: `wtw-diffusion-cli`
- Can be used in Azure DevOps pipelines
- Same functionality as VSIX but headless

---

## ?? File Migration Checklist

### Models ? Core
- [x] ActivityEntry.cs
- [x] DatabaseChange.cs
- [x] MigrationAction.cs
- [x] MigrationInfo.cs
- [x] ShadowDatabaseCacheInfo.cs
- [x] TableChangeSummary.cs

### Config ? Core
- [x] DataFoundryConfig.cs
- [x] TableListConfig.cs

### Services ? Core (Database)
- [x] SqlMigrationRepository.cs
- [x] ChangeDetectionService.cs
- [x] AzureSqlAuthenticationProvider.cs

### Services ? Core (Migration)
- [x] MigrationScriptManager.cs
- [x] MigrationScriptGenerator.cs
- [x] IMigrationExecutor.cs
- [x] CSharpMigrationExecutor.cs
- [x] PowerShellMigrationExecutor.cs
- [x] PowerShellScriptRunner.cs
- [x] PowerShellOutputParser.cs
- [x] PowerShellResult.cs

### Services ? Core (Storage)
- [x] ActivityHistoryService.cs
- [x] ChangeDetectionResultsService.cs
- [x] TrackedTablesManager.cs

### Helpers ? Core
- [x] PathHelper.cs (with modifications)

### Other ? Core
- [x] Constants.cs

### Stays in VSIX
- [ ] All Views/ (XAML)
- [ ] All Options/ (VS-specific settings)
- [ ] All Converters/ (XAML converters)
- [ ] ProjectFileManager.cs (uses DTE API)
- [ ] OutputWindowLogger.cs (uses VS Output window)
- [ ] GlobalProcessingStateService.cs (UI state management)
- [ ] DatabaseSyncStatusService.cs (UI state management)
- [ ] SettingsChangedService.cs (VS options integration)
- [ ] ServicesHelper.cs (VS-specific helpers)
- [ ] DataFoundryToolWindow.cs (VS window)
- [ ] DataFoundryToolWindowControl.xaml(.cs) (VS control)
- [ ] ShowDataFoundryToolWindowCommand.cs (VS command)
- [ ] data_foundryPackage.cs (VS package)
- [ ] AssemblyResolver.cs (VS-specific)

---

## ?? Success Criteria

1. ? Core library builds successfully (.NET Standard 2.0)
2. ? VSIX builds and runs in experimental instance
3. ? All existing functionality works identically
4. ? CLI tool builds and runs on Windows
5. ? CLI tool works in Azure DevOps pipeline
6. ? All unit tests pass (if applicable)
7. ? Documentation updated

---

## ?? Azure DevOps Integration Example

```yaml
# azure-pipelines.yml
steps:
  - task: UseDotNet@2
    inputs:
      version: '3.1.x'
  
  - script: dotnet tool install --global WTW.Diffusion.Cli
    displayName: 'Install WTW Diffusion CLI'
  
  - script: |
      wtw-diffusion-cli detect-changes \
        --target-server "$(SqlServer)" \
        --target-database "$(SqlDatabase)" \
        --migrations-path "$(Build.SourcesDirectory)/Database/Migrations" \
        --config-path "$(Build.SourcesDirectory)/config/tablelist.json"
    displayName: 'Detect Database Changes'
  
  - script: |
      wtw-diffusion-cli generate-script \
        --target-server "$(SqlServer)" \
        --target-database "$(SqlDatabase)" \
        --migrations-path "$(Build.SourcesDirectory)/Database/Migrations" \
        --script-name "Auto_Migration_$(Build.BuildId)" \
        --output-dir "$(Build.ArtifactStagingDirectory)"
    displayName: 'Generate Migration Script'
    condition: eq(variables['HasChanges'], 'true')
  
  - task: PublishBuildArtifacts@1
    inputs:
      PathtoPublish: '$(Build.ArtifactStagingDirectory)'
      ArtifactName: 'migration-scripts'
```

---

## ?? Notes

- **Backwards Compatibility:** All existing VSIX functionality preserved
- **Future-Proof:** Architecture supports future platforms (Linux, macOS, Docker)
- **DRY Principle:** Core business logic written once, used everywhere
- **Testability:** Core library can be unit tested independently
- **Maintenance:** Changes to business logic only need to be made in one place

---

**Document Version:** 1.0  
**Last Updated:** January 30, 2025  
**Status:** Ready for implementation ??
