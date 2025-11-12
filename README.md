# DataFoundry - SQL Change Automation Extension

A Visual Studio extension similar to RedGate SQL Change Automation for managing database changes and deployments.

## Features

### Current Implementation (with Dummy Data)

#### 1. Overview Tab
- **Project Information**: Displays project name, target server, database, and last deployment
- **Quick Actions**: Buttons for refreshing changes, comparing databases, and deploying
- **Recent Activity**: Timeline of recent deployment and comparison activities

#### 2. Changes Tab
- **Database Changes Grid**: DataGrid showing all detected database changes
  - Columns: Type, Object Name, Schema, Change Type, Modified Date
- **Filtering**: Filter changes by object type (Tables, Views, Stored Procedures, Functions, Triggers)
- **Change Counter**: Displays total number of changes
- **Refresh Button**: Manually trigger change detection

#### 3. Deployment Tab
- **Deployment Configuration**:
  - Server name
  - Database name
  - Authentication method (Windows/SQL Server)
  - Backup option before deployment
- **Deployment Options**:
  - Generate script only
  - Include transactions
  - Drop objects not in source
  - Ignore extended properties
- **Deployment Log**: Console-style output showing deployment progress
- **Deploy Button**: Execute deployment process

#### 4. Settings Tab
- **PowerShell Configuration**:
  - Scripts path with browse dialog
  - Execution policy selection
- **General Settings**:
  - Auto-refresh on project load
  - Deployment notifications
  - Verbose logging

## UI Design

The extension follows modern Visual Studio design patterns:
- Clean tabbed interface
- Professional color scheme matching VS 2022
- Styled buttons (Primary/Secondary)
- Responsive grid layouts
- Scrollable content areas

## Code Structure

```
data-foundry/
??? DataFoundryToolWindowControl.xaml      # Main UI definition
??? DataFoundryToolWindowControl.xaml.cs   # UI logic and event handlers
??? DataFoundryToolWindow.cs               # Tool window wrapper
??? data_foundryPackage.cs                 # VS Package registration
??? ShowDataFoundryToolWindowCommand.cs    # Command handler
??? DataFoundry.vsct                       # Menu command definition
??? Models/
    ??? DatabaseChange.cs                  # Data model for changes
```

## Dummy Data

Currently using hardcoded dummy data:
- 8 sample database changes (Tables, SPs, Views, Functions, Triggers)
- Sample project information
- Sample recent activity log

## Future Integration with PowerShell

The following methods are placeholders for PowerShell integration:

### RefreshChangesButton_Click
Will execute PowerShell scripts to:
- Scan database for schema changes
- Compare with source control
- Populate the Changes grid with real data

### CompareDatabasesButton_Click
Will execute PowerShell scripts to:
- Compare two database schemas
- Generate difference report
- Highlight conflicts

### DeployChangesButton_Click
Will execute PowerShell scripts to:
- Generate deployment scripts
- Create database backup (if enabled)
- Execute deployment
- Log all activities

### PowerShell Script Integration Points

1. **Change Detection**: `Get-DatabaseChanges.ps1`
2. **Database Comparison**: `Compare-DatabaseSchemas.ps1`
3. **Deployment**: `Deploy-DatabaseChanges.ps1`
4. **Backup**: `Backup-Database.ps1`

## How to Use

1. Open Visual Studio 2022
2. Go to **View** ? **DataFoundry**
3. The tool window will open with multiple tabs
4. Navigate between tabs to:
   - View project overview
   - See detected changes
   - Configure and execute deployments
   - Adjust settings

## Next Steps

1. Integrate PowerShell execution engine
2. Connect to real databases
3. Implement actual change detection
4. Add deployment script generation
5. Persist settings to user configuration
6. Add error handling and validation
7. Implement progress indicators for long-running operations

## Technologies Used

- .NET Framework 4.7.2
- WPF (Windows Presentation Foundation)
- Visual Studio SDK 17.0
- VSSDK Build Tools

## License

[Add your license here]
