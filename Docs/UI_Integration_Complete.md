# UI Integration Guide - SqlMigrationOrchestrator

## Overview

The Data Foundry UI has been fully wired to the `SqlMigrationOrchestrator`, replacing placeholder/dummy code with real database migration functionality. All three main tabs now execute actual migrations, detect changes, and deploy to your databases.

## Components Added

### 1. OutputWindowLogger (`Services/OutputWindowLogger.cs`)

A dedicated logger that writes to the Visual Studio Output window in a custom "Data Foundry" pane.

**Features:**
- `Log(string)` - Standard logging
- `LogError(string)` - Error logging
- `LogWarning(string)` - Warning logging
- `Clear()` - Clears the output pane
- `Show()` - Activates and shows the output pane

**Usage:**
```csharp
OutputWindowLogger.Show();
OutputWindowLogger.Log("Starting migration...");
OutputWindowLogger.LogError("Migration failed!");
```

## Updated UI Controls

### 2. OverviewTabControl (`Views/Controls/OverviewTabControl.xaml.cs`)

**Buttons Wired:**

#### Refresh Changes Button
- **Action**: Detects data changes between target and shadow databases
- **Process**:
  1. Clears output window
  2. Creates SqlMigrationOrchestrator from global settings
  3. Executes `DetectAndHandleChanges()`
  4. Displays summary in message box
- **Async**: Yes
- **Thread-safe**: Yes

#### Compare Databases Button
- **Action**: Same as Refresh Changes but shows detailed dialog
- **Process**: Identical to Refresh Changes + shows table-by-table breakdown
- **Async**: Yes
- **Thread-safe**: Yes

#### Deploy Button
- **Action**: Executes all pending migrations on target database
- **Process**:
  1. Clears output window and shows it
  2. Creates SqlMigrationOrchestrator
  3. Executes `ExecuteTargetMigrations()`
  4. Logs each migration executed
  5. Shows success/failure message
- **Async**: Yes
- **Thread-safe**: Yes

**Key Features:**
- Button state management (disabled during processing)
- Real-time logging to Output window
- Exception handling with user-friendly messages
- Progress indication through button text changes

### 3. ChangesTabControl (`Views/Controls/ChangesTabControl.xaml.cs`)

**Major Changes:**
- Removed dummy data (`DatabaseChange` objects)
- Now uses real `TableChangeSummary` from change detection
- Updated DataGrid to show: Table, Inserts, Updates, Deletes, HasChanges

**Refresh Changes Button:**
- **Action**: Detects and displays data changes in the grid
- **Process**:
  1. Calls `DetectAndHandleChanges()`
  2. Populates grid with `TableChangeSummary` objects
  3. Shows count of tables with changes
- **Async**: Yes
- **Thread-safe**: Yes

**Filter ComboBox:**
- **All Changes**: Shows all tracked tables
- **With Changes Only**: Shows only tables with inserts/updates/deletes
- **No Changes**: Shows tables with no changes

**XAML Changes:**
- Updated DataGrid columns to bind to `TableChangeSummary` properties
- Changed filter options to match new data type

### 4. DeploymentTabControl (`Views/Controls/DeploymentTabControl.xaml.cs`)

**Deploy Changes Button:**
- **Action**: Executes full migration workflow
- **Process**:
  1. Validates server/database inputs
  2. Shows confirmation dialog
  3. Executes `orchestrator.Execute()`
  4. Logs to both Output window and deployment log textbox
  5. Shows success/failure message
- **Async**: Yes
- **Thread-safe**: Yes

**Note**: Currently uses connection from `DataFoundryOptions` (Tools > Options > Data Foundry). The server/database fields in the UI are displayed but not yet used. You can enhance this to override settings if needed.

## Workflow Examples

### Example 1: Execute Migrations (Deploy Button)

```csharp
private async Task ExecuteMigrationsAsync()
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

    try
    {
        OutputWindowLogger.Clear();
        OutputWindowLogger.Show();
        OutputWindowLogger.Log("=== Starting Migration Execution ===");

        var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

        await Task.Run(() =>
        {
            orchestrator.ExecuteTargetMigrations(
                requireConfirmation: false,
                logger: msg =>
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        OutputWindowLogger.Log(msg);
                    });
                });
        });

        OutputWindowLogger.Log("=== Migration Execution Complete ===");
        
        MessageBox.Show("Migrations executed successfully!");
    }
    catch (Exception ex)
    {
        OutputWindowLogger.LogError($"Migration failed: {ex.Message}");
        MessageBox.Show($"Migration failed: {ex.Message}");
    }
}
```

### Example 2: Detect Changes (Refresh Changes Button)

```csharp
private async Task DetectChangesAsync()
{
    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

    OutputWindowLogger.Show();
    var orchestrator = SqlMigrationOrchestratorFactory.CreateFromGlobalPackage();

    List<TableChangeSummary> changes = null;

    await Task.Run(() =>
    {
        changes = orchestrator.DetectAndHandleChanges(
            action: null,
            logger: msg => OutputWindowLogger.Log(msg));
    });

    // Update UI with results
    _allChanges.Clear();
    foreach (var change in changes)
    {
        _allChanges.Add(change);
    }
}
```

## Thread Safety

All UI operations follow Visual Studio threading best practices:

1. **SwitchToMainThreadAsync** before accessing UI elements
2. **Task.Run** for long-running operations
3. **JoinableTaskFactory** for proper async/await
4. **Exception handling** in all async methods

## Progress Indication

Progress is indicated through:
- **Button State**: Buttons disabled during processing
- **Button Text**: "Refresh Changes..." / "Deploying..." / etc.
- **Output Window**: Real-time log messages
- **Message Boxes**: Completion/error dialogs

## Error Handling

All operations have comprehensive error handling:
- Try/catch blocks around all async operations
- Errors logged to Output window with stack trace
- User-friendly error messages in dialogs
- Processing flag (`_isProcessing`) prevents concurrent operations

## Configuration Source

All operations use settings from:
- **Tools > Options > Data Foundry**
- LocalDatabaseConnection ? Target database
- ShadowDatabaseConnection ? Shadow database (or auto-derived)
- SqlProject ? SQL project to use
- MigrationsFolder ? Location of migration scripts

## Testing the Integration

### 1. Test Migration Execution
1. Open Data Foundry tool window (View > Data Foundry)
2. Go to **Overview** tab
3. Click **Deploy** button
4. Watch Output window for migration logs
5. Verify migrations executed in target database

### 2. Test Change Detection
1. Make data changes in tracked tables
2. Click **Refresh Changes** or **Compare Databases**
3. Check Output window for detected changes
4. Go to **Changes** tab
5. Click **Refresh Changes**
6. Verify grid shows table changes

### 3. Test Deployment Tab
1. Go to **Deployment** tab
2. Enter server/database (currently informational only)
3. Click **Deploy Changes**
4. Watch deployment log
5. Verify success message

## Output Window Screenshot

When operations run, you'll see:
```
[14:32:15] === Starting Migration Execution ===
[14:32:15] Ensuring target database
[14:32:15] Ensuring migration log table
[14:32:16] Executing migration a1b2c3d4-... (001_InitialSchema.sql) on MyDatabase
[14:32:17] Executing migration e5f6g7h8-... (002_AddUsersTable.sql) on MyDatabase
[14:32:18] Migrations complete.
[14:32:18] === Migration Execution Complete ===
```

## Next Steps

Consider adding:
1. **Progress bars** for long-running operations
2. **Cancel button** to abort operations
3. **History/audit log** of executed migrations
4. **Rollback functionality** for failed deployments
5. **Diff viewer** to show data changes before applying
6. **Settings override** - use server/database from Deployment tab UI
7. **Backup creation** before deployments
8. **Scheduled migrations** - run migrations on a schedule

## Benefits

? **Real Functionality**: No more dummy data or placeholders  
? **Thread-Safe**: All async operations properly handled  
? **User Feedback**: Real-time logging and progress indication  
? **Error Handling**: Comprehensive exception handling  
? **VS Integration**: Uses Output window, threading model, settings  
? **Testable**: Can test with real databases  
? **Maintainable**: Clean separation of concerns

## Troubleshooting

### "SQL Project not found"
- Check Tools > Options > Data Foundry > SQL Project setting
- Ensure the SQL project is loaded in solution

### "MigrationsPath not found"
- Check Tools > Options > Data Foundry > Migrations Folder
- Ensure folder exists in SQL project

### "ConfigPath not found"
- The config file is now automatically created in the extension's installation directory
- Configure tracked tables via **Tools > Options > Data Foundry > Change Detection > Tracked Tables**
- Or manually edit `tablelist.json` in the extension installation folder
- Config location: `C:\Users\{YourName}\AppData\Local\Microsoft\VisualStudio\{Version}\Extensions\{ExtensionGuid}\tablelist.json`

### "Connection failed"
- Verify LocalDatabaseConnection in settings
- Test connection string manually
- For Azure SQL, ensure you're logged into Azure

### Output window not showing
- Check View > Output
- Select "Data Foundry" from the dropdown
- Or operations will auto-show it
