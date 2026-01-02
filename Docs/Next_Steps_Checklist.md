# Data Foundry - Next Steps Checklist

This document outlines potential improvements and features to implement in the Data Foundry Visual Studio extension.

## Status Legend
- ? Completed
- ?? In Progress
- ? Not Started
- ?? Nice to Have

---

## Core Functionality Improvements

### Overview Tab Enhancements

- [x] ? **Populate Source Database Field**
  - ~~Currently shows placeholder text `"[(LOCALDB)\CORE].[Core.Db]"`~~
  - ~~Should read from `DataFoundryOptions.LocalDatabaseConnection`~~
  - ~~Parse and display in user-friendly format: `[Server].[Database]`~~
  - **COMPLETED:** Now displays actual connection from settings in format `[Server].[Database]`
  - **Files modified:**
    - `Views/Controls/OverviewTabControl.xaml.cs` - `RefreshOverviewSettingsDisplay()` method
  - **Complexity:** Low
  - **Priority:** Medium

- [x] ? **Initialize Last Deployment Timestamp**
  - ~~Currently shows hardcoded `"2025-01-11 14:23:45"`~~
  - **COMPLETED:** Loads from most recent successful deployment in activity history
  - Shows "Never" if no deployments exist
  - **Files modified:**
    - `Views/Controls/OverviewTabControl.xaml.cs` - Added `InitializeLastDeployment()` method
    - Auto-updates when new deployments complete via `OnActivityAdded` event
  - **Complexity:** Low
  - **Priority:** Medium

- [ ] ? **Add Clear History Button**
  - Add button to clear activity history
  - Useful for testing and cleanup
  - Should prompt for confirmation
  - **Files to modify:**
    - `Views/Controls/OverviewTabControl.xaml` - Add button to UI
    - `Views/Controls/OverviewTabControl.xaml.cs` - Add click handler
    - `Services/ActivityHistoryService.cs` - Already has `Clear()` method
  - **Complexity:** Low
  - **Priority:** Low

- [ ] ? **Add Activity Type Filter**
  - Similar to Changes tab filter
  - Filter by: All, Deployments, Change Detection, Failures Only, etc.
  - **Files to modify:**
    - `Views/Controls/OverviewTabControl.xaml` - Add ComboBox filter
    - `Views/Controls/OverviewTabControl.xaml.cs` - Add filter logic
  - **Complexity:** Medium
  - **Priority:** Low

### Database Comparison Features

- [ ] ? **Implement Dedicated Database Comparison Dialog**
  - Currently "Compare Databases" button does the same as "Refresh Changes"
  - Create side-by-side comparison view
  - Show schema differences, data differences, or both
  - **Files to create:**
    - `Views/Dialogs/DatabaseComparisonDialog.xaml`
    - `Views/Dialogs/DatabaseComparisonDialog.xaml.cs`
  - **Files to modify:**
    - `Views/Controls/OverviewTabControl.xaml.cs` - Update `CompareDatabasesButton_Click`
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **Schema Comparison View**
  - Compare table structures, columns, indexes, constraints
  - Highlight differences
  - Generate ALTER scripts
  - **Complexity:** High
  - **Priority:** Low

### Deployment Features

- [ ] ? **Implement Database Backup**
  - DeploymentTabControl has "Create Backup" checkbox (not yet implemented)
  - Create backup before deployment
  - Store backup file path in activity history
  - **Files to modify:**
    - `Views/Controls/DeploymentTabControl.xaml.cs` - `ExecuteDeploymentAsync()` method
  - **Files to create:**
    - `Services/DatabaseBackupService.cs`
  - **Complexity:** Medium
  - **Priority:** High

- [ ] ? **Rollback Functionality**
  - Ability to rollback failed deployments
  - Restore from backup
  - Revert migration entries from `__MigrationLog`
  - **Files to create:**
    - `Services/RollbackService.cs`
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **Pre-deployment Validation**
  - Validate migration scripts before execution
  - Check for syntax errors
  - Detect potential breaking changes
  - **Complexity:** High
  - **Priority:** Low

### Change Detection & Migration Generation

- [x] ? **Migration Script Generation UI**
  - **COMPLETED:** Added "Generate Script" button to Changes tab
  - Auto-generates script name in format: `001_US000000_1_{timestamp}.sql`
  - Automatically determines next migration number by scanning existing scripts
  - Confirms with user before generating
  - Logs generated script to `__MigrationLog` table
  - Shows success dialog with file location
  - Tracks activity in activity history
  - **Files created/modified:**
    - `Views/Controls/ChangesTabControl.xaml` - Added Generate Script button
    - `Views/Controls/ChangesTabControl.xaml.cs` - Implemented generation logic
    - `Services/SqlMigrationOrchestrator.cs` - Added `GenerateMigrationScriptWithName()` method
  - **Complexity:** Medium
  - **Priority:** High

- [ ] ? **Diff Viewer for Data Changes**
  - Show before/after data values
  - Highlight changed fields
  - Allow user to review before applying
  - **Files to create:**
    - `Views/Dialogs/DataDiffViewer.xaml`
    - `Views/Dialogs/DataDiffViewer.xaml.cs`
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **Selective Change Application**
  - Allow user to select which changes to migrate
  - Check/uncheck tables or specific rows
  - Generate migration script only for selected changes
  - **Complexity:** High
  - **Priority:** Low

---

## UI/UX Improvements

### General Polish

- [ ] ? **Progress Bars for Long Operations**
  - Add progress indicators for deployments, change detection
  - Show estimated time remaining
  - **Files to modify:**
    - All tab controls (OverviewTabControl, ChangesTabControl, DeploymentTabControl)
  - **Complexity:** Medium
  - **Priority:** Medium

- [ ] ? **Cancel Button for Operations**
  - Allow user to abort long-running operations
  - Use CancellationToken pattern
  - **Files to modify:**
    - `Services/SqlMigrationOrchestrator.cs`
    - All tab controls
  - **Complexity:** Medium
  - **Priority:** Low

- [ ] ? **Toast Notifications**
  - Show non-intrusive notifications for operation completion
  - Option to disable in settings
  - **Files to modify:**
    - `Options/DataFoundryOptions.cs` - Already has `ShowNotifications` setting
  - **Files to create:**
    - `Services/NotificationService.cs`
  - **Complexity:** Low
  - **Priority:** Low

- [ ] ? **Dark Theme Support**
  - Respect Visual Studio theme (light/dark)
  - Update colors and styles accordingly
  - **Files to modify:**
    - `Styles/ControlStyles.xaml`
  - **Complexity:** Medium
  - **Priority:** Low

### Activity History Enhancements

- [ ] ?? **Click to View Full Activity Details**
  - Show dialog with complete activity information
  - Include stack trace for errors
  - Show duration, timestamps, etc.
  - **Files to create:**
    - `Views/Dialogs/ActivityDetailsDialog.xaml`
    - `Views/Dialogs/ActivityDetailsDialog.xaml.cs`
  - **Complexity:** Low
  - **Priority:** Nice to Have

- [ ] ?? **Export Activity History to CSV**
  - Export for reporting or analysis
  - Include filters (date range, status, type)
  - **Files to create:**
    - `Services/ActivityExportService.cs`
  - **Complexity:** Low
  - **Priority:** Nice to Have

- [ ] ?? **Activity Statistics Dashboard**
  - Show success rate, average duration
  - Charts/graphs of activity over time
  - Most common errors
  - **Files to create:**
    - `Views/Controls/StatisticsTabControl.xaml`
    - `Views/Controls/StatisticsTabControl.xaml.cs`
  - **Complexity:** High
  - **Priority:** Nice to Have

---

## Configuration & Settings

### Options Page Enhancements

- [ ] ? **Activity History Settings**
  - Max entries to keep (currently hardcoded to 100)
  - Auto-clear history on Visual Studio close
  - Choose persistence location
  - **Files to modify:**
    - `Options/DataFoundryOptions.cs`
    - `Services/ActivityHistoryService.cs`
  - **Complexity:** Low
  - **Priority:** Low

- [ ] ? **Connection String Testing**
  - Add "Test Connection" button in options
  - Validate connection strings before saving
  - **Files to modify:**
    - `Options/DataFoundryOptions.cs`
    - `Options/ConnectionStringEditor.cs`
  - **Complexity:** Low
  - **Priority:** Medium

- [ ] ? **Tracked Tables UI Improvement**
  - Better UI for managing tracked tables
  - Add/remove tables with autocomplete
  - Load from database schema
  - **Files to modify:**
    - `Options/DataFoundryOptions.cs`
  - **Files to create:**
    - `Options/TrackedTablesEditor.cs`
  - **Complexity:** Medium
  - **Priority:** Medium

---

## Advanced Features

### Automation & Scheduling

- [ ] ? **Scheduled Migrations**
  - Run migrations on a schedule (e.g., nightly)
  - Integration with CI/CD pipelines
  - **Complexity:** High
  - **Priority:** Low

- [ ] ? **Git Integration**
  - Auto-commit generated migration scripts
  - Tag releases with migration versions
  - **Complexity:** High
  - **Priority:** Low

### Multi-Environment Support

- [ ] ? **Environment Profiles**
  - Dev, QA, Staging, Production environments
  - Switch between environments easily
  - Environment-specific settings
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **Deployment Pipeline Visualization**
  - Show migration status across environments
  - Track which migrations are deployed where
  - **Complexity:** Very High
  - **Priority:** Low

### Collaboration Features

- [ ] ? **Conflict Detection**
  - Detect when multiple developers generate conflicting migrations
  - Suggest merge strategies
  - **Complexity:** Very High
  - **Priority:** Low

- [ ] ? **Migration Comments/Annotations**
  - Add notes to migrations
  - Link to work items or tickets
  - **Complexity:** Medium
  - **Priority:** Low

---

## Testing & Quality

### Testing Improvements

- [ ] ? **Unit Tests**
  - Add unit tests for core services
  - Test activity tracking, path validation, etc.
  - **Complexity:** Medium
  - **Priority:** High

- [ ] ? **Integration Tests**
  - Test full deployment workflows
  - Test with real databases (LocalDB)
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **UI Tests**
  - Automated UI testing for dialogs and controls
  - **Complexity:** High
  - **Priority:** Low

### Error Handling

- [ ] ? **Better Error Messages**
  - More descriptive error messages
  - Suggested fixes for common errors
  - **Complexity:** Low
  - **Priority:** Medium

- [ ] ? **Diagnostic Logging**
  - Enhanced logging for troubleshooting
  - Log to file option
  - Use `VerboseLogging` setting
  - **Complexity:** Low
  - **Priority:** Low

---

## Documentation

### User Documentation

- [ ] ? **Quick Start Video**
  - Create screencast tutorial
  - Show common workflows
  - **Complexity:** Medium
  - **Priority:** Low

- [ ] ? **FAQ Document**
  - Common issues and solutions
  - Best practices
  - **Complexity:** Low
  - **Priority:** Low

- [ ] ? **API Documentation**
  - Document public APIs for extensibility
  - XML comments on all public methods
  - **Complexity:** Medium
  - **Priority:** Low

### Developer Documentation

- [ ] ? **Architecture Guide**
  - Document design decisions
  - Component interaction diagrams
  - **Complexity:** Medium
  - **Priority:** Low

- [ ] ? **Contributing Guide**
  - How to contribute to the project
  - Code style guidelines
  - **Complexity:** Low
  - **Priority:** Low

---

## Performance & Optimization

- [ ] ? **Large Dataset Handling**
  - Optimize for tables with millions of rows
  - Pagination for change detection
  - **Complexity:** High
  - **Priority:** Low

- [ ] ? **Caching Strategy**
  - Cache database metadata
  - Cache schema information
  - **Complexity:** Medium
  - **Priority:** Low

- [ ] ? **Async/Await Optimization**
  - Review all async operations
  - Ensure proper ConfigureAwait usage
  - **Complexity:** Low
  - **Priority:** Low

---

## Security

- [ ] ? **Credential Management**
  - Secure storage of connection strings
  - Integration with Windows Credential Manager
  - **Complexity:** High
  - **Priority:** Medium

- [ ] ? **Audit Trail**
  - Track who performed which operations
  - Integration with organizational compliance
  - **Complexity:** High
  - **Priority:** Low

---

## Packaging & Distribution

- [ ] ? **Publish to Visual Studio Marketplace**
  - Create marketplace listing
  - Screenshots, description, etc.
  - **Complexity:** Low
  - **Priority:** High

- [ ] ? **Auto-Update Mechanism**
  - Notify users of new versions
  - One-click update
  - **Complexity:** Medium
  - **Priority:** Low

- [ ] ? **Telemetry (Optional)**
  - Anonymous usage statistics
  - Help improve the extension
  - Opt-in only
  - **Complexity:** Medium
  - **Priority:** Low

---

## Immediate Priorities (Recommended Order)

1. ? **Activity Tracking System** - COMPLETED!
2. ? **Populate Overview Tab Fields** (Source Database, Last Deployment) - COMPLETED!
3. ? **Migration Script Generation UI** - COMPLETED!
4. **Database Backup Implementation**
5. **Connection String Testing**
6. **Publish to Marketplace**

---

## Notes

- This checklist is a living document and should be updated as priorities change
- Mark items as completed (?) when finished
- Add new ideas as they come up
- Consider user feedback when prioritizing

---

**Last Updated:** 2025-01-11  
**Next Review Date:** TBD
