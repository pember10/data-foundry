# SQL Injection Prevention in SqlMigrationRepository

## Overview

The `SqlMigrationRepository` class has been hardened against SQL injection attacks by replacing string concatenation with parameterized queries and safe identifier quoting.

## Security Improvements

### ? Fixed Methods

| Method | Vulnerability | Solution |
|--------|--------------|----------|
| `DatabaseExists()` | Database name in `DB_ID()` | Parameterized with `@DatabaseName` |
| `LogMigrationExecution()` | Migration ID, checksum, filename | Parameterized with `@MigrationId`, `@Checksum`, `@FileName` |
| `GetPrimaryKeyColumns()` | Table name in `OBJECT_NAME()` | Parameterized with `@TableName` |
| `GetNonPrimaryColumns()` | Table name in `OBJECT_ID()` | Parameterized with `@TableName` and `@ObjectId` |
| `GetColumnMetadata()` | Database and table names | `QuoteSqlIdentifier()` helper |
| `GetTableData()` | Database and table names | `QuoteSqlIdentifier()` helper |
| `CreateDatabaseIfMissing()` | Database name in DDL | `QuoteSqlIdentifier()` helper |
| `DropAndRecreateDatabase()` | Database name in DDL | `QuoteSqlIdentifier()` helper |

### ?? New Features

#### 1. Parameterized Query Overloads

```csharp
// New overload accepting parameters
public DataTable ExecuteQuery(string database, string query, params SqlParameter[] parameters)

// New overload accepting parameters
public int ExecuteNonQuery(string database, string query, params SqlParameter[] parameters)
```

**Usage Example:**
```csharp
var result = ExecuteQuery("MyDb", 
    "SELECT * FROM Users WHERE UserId = @UserId", 
    new SqlParameter("@UserId", userId));
```

#### 2. Safe Identifier Quoting

```csharp
private string QuoteSqlIdentifier(string identifier)
```

**What it does:**
- Wraps identifiers in `[brackets]` to prevent injection
- Validates for malicious characters
- Prevents double-quoting

**Usage Example:**
```csharp
var safeName = QuoteSqlIdentifier("MyTable"); // Returns: [MyTable]
var query = $"SELECT * FROM {safeName}";
```

## Before vs. After

### ? Before (Vulnerable)

```csharp
public bool DatabaseExists(string database)
{
    var query = $"SELECT CASE WHEN DB_ID('{database}') IS NULL THEN 0 ELSE 1 END AS exists_flag;";
    var result = ExecuteQuery("master", query);
    // ...
}
```

**Attack Vector:**
```csharp
DatabaseExists("'; DROP DATABASE Production; --")
// Executes: SELECT CASE WHEN DB_ID(''; DROP DATABASE Production; --') IS NULL...
```

### ? After (Secure)

```csharp
public bool DatabaseExists(string database)
{
    var query = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END AS exists_flag;";
    var result = ExecuteQuery("master", query, new SqlParameter("@DatabaseName", database));
    // ...
}
```

**Attack Prevented:**
```csharp
DatabaseExists("'; DROP DATABASE Production; --")
// Parameter is safely escaped - no SQL injection possible
```

## Why Some Identifiers Use QuoteSqlIdentifier

### Database and Table Names Cannot Be Parameterized

SQL Server does **not** allow parameters for DDL statements or object identifiers in certain contexts:

```csharp
// ? This won't work:
CREATE DATABASE @DatabaseName;

// ? This works:
CREATE DATABASE [MyDatabase];
```

For these cases, we use `QuoteSqlIdentifier()` which:
1. Wraps the identifier in `[brackets]`
2. Validates for injection attempts
3. Prevents characters like `]` that could break quoting

### When to Use Each Approach

| Scenario | Use |
|----------|-----|
| WHERE clause values | `SqlParameter` |
| Column/table names in queries | `QuoteSqlIdentifier()` |
| DDL statements (CREATE, DROP, ALTER) | `QuoteSqlIdentifier()` |
| Function parameters (e.g., `DB_ID()`) | `SqlParameter` if supported, otherwise `QuoteSqlIdentifier()` |

## Remaining Safe Cases

Some methods still use string concatenation but are **safe** because:

### 1. Migration Script Content (`ExecuteNonQuery`)
```csharp
var ddl = File.ReadAllText(schemaScriptPath);
ExecuteNonQuery(database, ddl);
```
- Content comes from trusted migration files
- Not user input
- Intentionally allows arbitrary SQL

### 2. Hard-coded Queries
```csharp
var query = "SELECT migration_id FROM dbo.__MigrationLog";
```
- No user input
- No string concatenation

## Best Practices Going Forward

### ? DO

```csharp
// Use parameters for all user input
var result = ExecuteQuery(db, 
    "SELECT * FROM Users WHERE Email = @Email", 
    new SqlParameter("@Email", userEmail));

// Use QuoteSqlIdentifier for dynamic identifiers
var tableName = QuoteSqlIdentifier(userTableName);
var query = $"SELECT COUNT(*) FROM {tableName}";
```

### ? DON'T

```csharp
// Don't concatenate user input
var query = $"SELECT * FROM Users WHERE Email = '{userEmail}'";

// Don't trust unvalidated identifiers
var query = $"SELECT * FROM [{userTableName}]";
```

## Testing for SQL Injection

To verify the fixes work, try these attack strings:

```csharp
// Attack attempts that should now fail gracefully:
DatabaseExists("'; DROP DATABASE master; --")
GetTableData("MyDb", "Users]; DROP TABLE Users; --")
LogMigrationExecution("MyDb", guid, "script.sql'; DELETE FROM __MigrationLog; --", "hash")
```

All should either:
1. Execute safely (parameters are escaped)
2. Throw validation errors (identifier quoting rejects invalid characters)

## Additional Validation

Consider adding these optional validations:

```csharp
// Regex validation for identifiers
private bool IsValidIdentifier(string identifier)
{
    return Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
}

// Maximum length checks
private void ValidateIdentifierLength(string identifier, int maxLength = 128)
{
    if (identifier.Length > maxLength)
        throw new ArgumentException($"Identifier exceeds maximum length of {maxLength}");
}
```

## References

- [SQL Injection Prevention Cheat Sheet (OWASP)](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
- [SqlParameter Class Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlparameter)
- [QUOTENAME (Transact-SQL)](https://docs.microsoft.com/en-us/sql/t-sql/functions/quotename-transact-sql)
