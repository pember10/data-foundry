# SqlMigrationOrchestrator Integration with DataFoundryOptions

## Overview

The `SqlMigrationOrchestrator` now supports initialization directly from your Visual Studio extension's `DataFoundryOptions` settings. This eliminates the need to manually specify connection strings, migration paths, and other configuration when creating orchestrator instances.

## Mapping: DataFoundryOptions ? SqlMigrationOrchestrator

| DataFoundryOptions Property | SqlMigrationOrchestrator Field | Description |
|----------------------------|-------------------------------|-------------|
| `LocalDatabaseConnection` | `_targetDatabase`, `_targetServer` | Connection string is parsed to extract database name and server instance |
| `MigrationsFolder` | `_migrationsPath`, `_outputMigrationDir` | Resolved relative to the SQL project directory |
| `SqlProject` | *(used for path resolution)* | Used to locate the SQL project and determine base paths |

## Additional Resolved Paths

- **`_configPath`**: `{SqlProjectDir}/Scripts/config.json`
- **`_migrationLogDdlPath`**: `{SqlProjectDir}/Scripts/MigrationLogTableDefinition.sql`

## Usage Examples

### Option 1: Using the Factory (Recommended)

```csharp
using data_foundry.Services;
using Microsoft.VisualStudio.Shell;

// From within a VS extension command or tool window
ThreadHelper.ThrowIfNotOnUIThread();

// Create orchestrator from global package instance
var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

// Execute migrations
orchestrator.ExecuteTargetMigrations(requireConfirmation: false);

// Or detect and handle changes
orchestrator.Execute(
    confirmTargetMigration: false,
    detectChanges: true,
    action: data_foundry.Models.MigrationAction.Migrate
);
```

### Option 2: Direct Constructor (if you have DTE reference)

```csharp
using data_foundry.Services;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

ThreadHelper.ThrowIfNotOnUIThread();

var package = data_foundryPackage.Instance;
var options = package.GetDialogPage(typeof(DataFoundryOptions)) as DataFoundryOptions;
var dte = Package.GetGlobalService(typeof(DTE)) as DTE;

var orchestrator = new SqlMigrationOrchestrator(options, dte);
```

### Option 3: Traditional Constructor (for PowerShell or standalone scenarios)

```csharp
var orchestrator = new SqlMigrationOrchestrator(
    targetDatabase: "AppDb",
    targetServer: @".\SQLEXPRESS",
    migrationsPath: @"D:\path\to\Migrations"
);
```

## Connection String Parsing

The orchestrator automatically parses the `LocalDatabaseConnection` to extract:
- **Initial Catalog** ? Target database name
- **Data Source** ? Target server instance

Example connection string:
```
Data Source=(LocalDb)\Core;Initial Catalog=Core.Db;Integrated Security=True
```

Extracted values:
- Database: `Core.Db`
- Server: `(LocalDb)\Core`

## Shadow Database

The shadow database connection is automatically derived as `{TargetDatabase}_Shadow`. If you need explicit control over the shadow database connection, use the `ShadowDatabaseConnection` property from `DataFoundryOptions` (requires additional enhancement).

## Future Enhancements

To further integrate with DataFoundryOptions, consider:

1. **Shadow Database Connection**: Use `ShadowDatabaseConnection` if explicitly set
2. **Verbose Logging**: Integrate with `VerboseLogging` option
3. **Notification Support**: Use `ShowNotifications` to display completion messages
4. **Auto-refresh**: Integrate with `AutoRefresh` for automatic change detection

## Thread Safety Note

When using the options-based constructor, ensure you're on the UI thread:

```csharp
ThreadHelper.ThrowIfNotOnUIThread();
```

This is required because:
- `DTE` operations require the UI thread
- `GetDialogPage()` may access UI resources
- Project enumeration uses COM interop

## Example: Tool Window Integration

```csharp
public class MyToolWindowControl : UserControl
{
    private async void OnDeployButtonClick(object sender, RoutedEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();
            
            // Execute with logging to output window
            orchestrator.Execute(
                confirmTargetMigration: false,
                detectChanges: true,
                action: MigrationAction.Migrate,
                logger: LogToOutputWindow
            );
        }
        catch (Exception ex)
        {
            // Handle errors
            MessageBox.Show($"Migration failed: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogToOutputWindow(string message)
    {
        // Your logging implementation
        Debug.WriteLine(message);
    }
}
```

## Benefits

? **Centralized Configuration**: All settings managed through VS Tools ? Options  
? **Type Safety**: Strong typing with IntelliSense support  
? **Maintainability**: Changes to settings automatically reflected in orchestrator  
? **Flexibility**: Both options-based and traditional constructors available  
? **Integration**: Seamless integration with existing VS extension architecture
