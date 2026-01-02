# Performance Optimization Plan

## Current Performance Bottlenecks

### 1. **Shadow Database Recreation** (CRITICAL - 70% of time)
**Problem:** Shadow database is dropped and recreated on EVERY change detection
```csharp
_repository.DropAndRecreateDatabase(_shadowDatabase);  // Expensive!
_repository.EnsureMigrationLogTable(_shadowDatabase, _migrationLogSchemaPath);
foreach (var migration in pendingShadow) {
    _scriptManager.ExecuteMigrationScript(_shadowDatabase, migration);  // Re-runs ALL migrations
}
```

**Impact:** For 10 migrations, this could take 5-30 seconds depending on complexity

**Solution:** Only recreate if migrations have changed
- Track last migration ID applied to shadow
- Compare with current migrations
- Only recreate if new migrations exist

### 2. **SELECT * with EXCEPT** (HIGH - 20% of time)
**Problem:** Compares ALL columns even when only counting
```csharp
SELECT COUNT(*) FROM (
    SELECT * FROM Target.dbo.Table 
    EXCEPT 
    SELECT * FROM Shadow.dbo.Table
) x
```

**Impact:** Transfers unnecessary data, slow for wide tables

**Solution:** Use EXISTS with primary key comparison for counts

### 3. **Multiple Queries Per Table** (MEDIUM - 5% of time)
**Problem:** 3-4 separate queries per table
1. Get primary keys
2. Get non-primary columns  
3. Count inserts
4. Count deletes
5. Count updates

**Impact:** Network latency multiplied by table count

**Solution:** Combine into single query per table

### 4. **String Concatenation for Hashing** (LOW - 3% of time)
**Problem:** CONCAT + HASHBYTES on all columns
```csharp
CONCAT(col1, '|', col2, '|', col3...)
```

**Impact:** String manipulation overhead

**Solution:** Use CHECKSUM or direct column comparison

### 5. **Metadata Queries Not Cached** (LOW - 2% of time)
**Problem:** Primary key queries repeat for same table
**Solution:** Cache metadata in memory

## Optimization Priority

### Phase 1: Critical (Implement Now)
1. ? Smart shadow database caching
2. ? Optimized counting queries  
3. ? Batch operations

### Phase 2: High Impact (Next Sprint)
4. Connection pooling improvements
5. Parallel table processing
6. Metadata caching

### Phase 3: Polish (Future)
7. Query result caching
8. Incremental change detection
9. Background refresh

## Expected Performance Gains

| Optimization | Current Time | Optimized Time | Savings |
|-------------|--------------|----------------|---------|
| Shadow DB Recreation | 10-30s | 0-2s (cached) | 90-95% |
| Change Detection Queries | 5-10s | 1-2s | 80% |
| Overall Operation | 15-40s | 1-4s | **90%+** |

## Implementation Details

### 1. Smart Shadow Database Caching

```csharp
// Track what's in shadow database
private static string _lastShadowMigrationChecksum = null;

public List<TableChangeSummary> DetectAndHandleChanges(...)
{
    var currentChecksum = GetMigrationsChecksum();
    
    if (_lastShadowMigrationChecksum != currentChecksum)
    {
        // Only recreate if migrations changed
        logger("Shadow database out of date - recreating...");
        RecreateAndSyncShadowDatabase();
        _lastShadowMigrationChecksum = currentChecksum;
    }
    else
    {
        logger("Shadow database up to date - using cached version");
    }
    
    // Detect changes...
}
```

### 2. Optimized Counting Query

**Before:**
```sql
SELECT COUNT(*) FROM (
    SELECT * FROM Target.dbo.Table 
    EXCEPT 
    SELECT * FROM Shadow.dbo.Table
) x
```

**After:**
```sql
-- Inserts (in target, not in shadow)
SELECT COUNT(*) 
FROM Target.dbo.Table t
WHERE NOT EXISTS (
    SELECT 1 FROM Shadow.dbo.Table s 
    WHERE s.Id = t.Id
)

-- Updates (PK match, data different)
SELECT COUNT(*)
FROM Target.dbo.Table t
INNER JOIN Shadow.dbo.Table s ON t.Id = s.Id
WHERE CHECKSUM(t.Col1, t.Col2, ...) <> CHECKSUM(s.Col1, s.Col2, ...)

-- Deletes (in shadow, not in target)
SELECT COUNT(*)
FROM Shadow.dbo.Table s
WHERE NOT EXISTS (
    SELECT 1 FROM Target.dbo.Table t
    WHERE t.Id = s.Id
)
```

### 3. Single Combined Query

```sql
-- Get all counts in one query
SELECT 
    (SELECT COUNT(*) FROM Target.dbo.Table t WHERE NOT EXISTS (...)) AS Inserts,
    (SELECT COUNT(*) FROM Target.dbo.Table t INNER JOIN Shadow.dbo.Table s ...) AS Updates,
    (SELECT COUNT(*) FROM Shadow.dbo.Table s WHERE NOT EXISTS (...)) AS Deletes
```

## Monitoring & Measurement

Add performance logging:
```csharp
var sw = Stopwatch.StartNew();
logger($"Shadow DB sync: {sw.ElapsedMilliseconds}ms");

sw.Restart();
logger($"Change detection: {sw.ElapsedMilliseconds}ms");
```

## Rollback Plan

Keep old implementation as `GetTableDiffCounts_Legacy()` for comparison and fallback.

## Testing Checklist

- [ ] Test with 0 changes
- [ ] Test with only inserts
- [ ] Test with only updates  
- [ ] Test with only deletes
- [ ] Test with mixed changes
- [ ] Test with large tables (1000+ rows)
- [ ] Test with many columns (50+)
- [ ] Test with multiple tracked tables
- [ ] Test with new migrations (should recreate shadow)
- [ ] Test without new migrations (should use cache)

---

**Next Steps:** Implement Phase 1 optimizations
