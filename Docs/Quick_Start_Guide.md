# Quick Start Guide - Data Foundry UI

## Setup (One-Time)

1. **Configure Settings**
   - Go to **Tools** > **Options** > **Data Foundry**
   - Set **Local Database Connection** (e.g., `Data Source=(LocalDb)\Core;Initial Catalog=MyDb;Integrated Security=True`)
   - Set **SQL Project** name (e.g., `MyApp.Database`)
   - Set **Migrations Folder** (e.g., `Migrations`)
   - Click **OK**

2. **Ensure Configuration Files Exist**
   - **Tracked Tables**: Configure via **Tools > Options > Data Foundry > Change Detection > Tracked Tables**
   - Enter comma-separated table names (e.g., `Users, Orders, Products`)
   - The extension will create `tablelist.json` automatically in its installation folder
   - **Migration Log Schema**: Automatically included with the extension

3. **Open Data Foundry**
   - Go to **View** > **Data Foundry**
   - Tool window will open

## Daily Workflow

### Scenario 1: Execute New Migrations

**When**: You've added new migration scripts to your Migrations folder

**Steps:**
1. Open Data Foundry tool window
2. Go to **Overview** tab (or **Deployment** tab)
3. Click **Deploy** (or **Deploy Changes**)
4. Confirm in dialog
5. Watch Output window for progress
6. Verify success message

**What it does:**
- Reads all `.sql` files in Migrations folder
- Identifies pending migrations (not in `__MigrationLog`)
- Executes them in order
- Logs each execution to `__MigrationLog`

---

### Scenario 2: Detect Data Changes

**When**: You've modified data in tracked tables and want to generate a migration

**Steps:**
1. Open Data Foundry tool window
2. Go to **Changes** tab
3. Click **Refresh Changes**
4. Review changes in grid:
   - **Table**: Name of the table
   - **Inserts**: Number of rows to insert
   - **Updates**: Number of rows to update
   - **Deletes**: Number of rows to delete
   - **Has Changes**: Checkbox indicating if table has changes
5. Optionally use filter dropdown:
   - **All Changes**: Show all tracked tables
   - **With Changes Only**: Show only tables with changes
   - **No Changes**: Show tables with no changes

**What it does:**
- Creates shadow database (`{YourDatabase}_Shadow`)
- Applies all migrations to shadow
- Compares data between target and shadow for tracked tables
- Shows differences

---

### Scenario 3: Compare Databases

**When**: You want a quick summary of data differences

**Steps:**
1. Go to **Overview** tab
2. Click **Compare Databases**
3. Review message box showing:
   - Tables with changes
   - Insert/Update/Delete counts per table

**What it does:**
- Same as Detect Changes
- Shows summary dialog instead of populating grid

---

### Scenario 4: Generate Migration from Data Changes

**When**: You want to create a migration script from data changes

> **Note**: This feature is partially implemented. Currently detects changes but prompting for script name and auto-generation requires UI enhancement.

**Current Workaround:**
1. Detect changes (Scenarios 2 or 3)
2. Manually create migration script based on Output window details

**Future Enhancement:**
- Add "Generate Script" button in Changes tab
- Prompt for migration name
- Auto-generate INSERT/UPDATE/DELETE statements
- Save to Migrations folder

---

## Understanding the Tabs

### Overview Tab
- **Quick Actions**: Refresh, Compare, Deploy
- **Settings Display**: Shows current SQL project
- **Best for**: Quick deployments and comparisons

### Changes Tab
- **Detailed Grid**: Shows per-table change counts
- **Filtering**: Filter by change status
- **Best for**: Reviewing specific data changes

### Deployment Tab
- **Full Deployment Log**: Scrollable text log
- **Server/Database Display**: Shows targets (currently informational)
- **Best for**: Watching detailed deployment progress

## Output Window Tips

1. **View Output Window**: **View** > **Output** (or `Ctrl+W, O`)
2. **Select Data Foundry Pane**: Dropdown at top of Output window
3. **Clear Output**: Operations auto-clear, or close/reopen window
4. **Copy Logs**: Select text and copy for reporting

## Common Workflows

### Initialize New Database
```
1. Create empty database manually
2. Configure connection in Tools > Options
3. Click Deploy to run all migrations
4. Verify __MigrationLog table created
```

### Add New Migration
```
1. Create new .sql file in Migrations folder
2. Add header: -- <Migration ID="new-guid-here" />
3. Write your SQL (CREATE TABLE, ALTER, etc.)
4. Save file
5. Click Deploy in Data Foundry
```

### Track Data Changes
```
1. Modify data in tracked tables (via SSMS or code)
2. Click Refresh Changes in Changes tab
3. Review grid showing differences
4. (Future) Click Generate Script
```

### Troubleshoot Failed Migration
```
1. Check Output window for error details
2. Fix the migration script
3. Update the script's GUID to make it "new"
4. Click Deploy again
```

## Keyboard Shortcuts

Currently no custom shortcuts. Use standard VS:
- **Show Tool Window**: Assign in Tools > Options > Keyboard > "DataFoundry"
- **Show Output**: `Ctrl+W, O`

## Tips & Tricks

### Tip 1: Keep Output Window Open
Pin the Output window and keep "Data Foundry" selected for real-time feedback.

### Tip 2: Use Filters in Changes Tab
Use "With Changes Only" filter to quickly see which tables need attention.

### Tip 3: Test in LocalDB First
Always test migrations in LocalDB before deploying to shared environments.

### Tip 4: Review __MigrationLog
Query `SELECT * FROM __MigrationLog ORDER BY complete_dt DESC` to see migration history.

### Tip 5: Shadow Database Cleanup
Shadow databases are dropped/recreated on each detection. Don't use them for manual work.

## Safety Features

? **Read-Only Operations**: Refresh/Compare don't modify data  
? **Confirmation Dialogs**: Deploy operations ask for confirmation  
? **Logging**: All operations logged to Output window  
? **Exception Handling**: Errors don't crash Visual Studio  
? **Transaction Safety**: Migrations run in transactions (if script supports it)

## Need Help?

- **Check Output Window**: Most errors have detailed logs
- **Verify Settings**: Tools > Options > Data Foundry
- **Check Config Files**: Ensure `tablelist.json` and migration scripts exist
- **Review Documentation**: See `Docs/UI_Integration_Complete.md`
