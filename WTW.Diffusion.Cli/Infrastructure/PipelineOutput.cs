using System.Text;
using WTW.Diffusion.Cli.Adapters;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Cli.Infrastructure;

/// <summary>
/// Writes a Markdown pipeline summary and emits Azure DevOps pipeline variables
/// based on the results of a migration run.
///
/// Variables set:
///   wtw.hasChanges          true | false
///   wtw.pendingMigrations   count of pending migrations applied
///   wtw.changedTables       comma-separated list of tables with changes
///   wtw.generatedScript     path to the generated migration script (if any)
///   wtw.totalChanges        total count of inserts + updates + deletes detected
/// </summary>
internal static class PipelineOutput
{
    /// <summary>
    /// Emits all pipeline variables and writes a Markdown summary file,
    /// then uploads it as an ADO pipeline summary tab.
    /// </summary>
    public static void Publish(
        int pendingMigrationsApplied,
        List<TableChangeSummary>? changes,
        string? generatedScriptPath,
        string tempDir)
    {
        var diffs = changes?.Where(c => c.HasChanges).ToList() ?? new List<TableChangeSummary>();
        bool hasChanges = diffs.Count > 0;
        int totalChanges = diffs.Sum(d => d.Inserts + d.Updates + d.Deletes);
        string changedTables = hasChanges
            ? string.Join(",", diffs.Select(d => d.Table))
            : string.Empty;

        // --- Pipeline variables -------------------------------------------
        AzdoLogger.SetVariable("wtw.hasChanges",         hasChanges.ToString().ToLowerInvariant());
        AzdoLogger.SetVariable("wtw.pendingMigrations",  pendingMigrationsApplied.ToString());
        AzdoLogger.SetVariable("wtw.changedTables",      changedTables);
        AzdoLogger.SetVariable("wtw.totalChanges",       totalChanges.ToString());

        if (!string.IsNullOrWhiteSpace(generatedScriptPath))
            AzdoLogger.SetVariable("wtw.generatedScript", generatedScriptPath);

        // --- Markdown summary ---------------------------------------------
        string summaryPath = Path.Combine(tempDir, "wtw-diffusion-summary.md");
        File.WriteAllText(summaryPath, BuildMarkdown(
            pendingMigrationsApplied, diffs, generatedScriptPath, totalChanges));

        AzdoLogger.UploadSummary(summaryPath);

        // --- Build tag -----------------------------------------------------
        if (hasChanges)
            AzdoLogger.AddBuildTag("data-changes-detected");
        if (pendingMigrationsApplied > 0)
            AzdoLogger.AddBuildTag("migrations-applied");
    }

    private static string BuildMarkdown(
        int pendingApplied,
        List<TableChangeSummary> diffs,
        string? generatedScript,
        int totalChanges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WTW Diffusion — Pipeline Summary");
        sb.AppendLine();

        // Migrations section
        sb.AppendLine("## Migrations");
        sb.AppendLine(pendingApplied > 0
            ? $"? **{pendingApplied}** pending migration(s) applied successfully."
            : "? No pending migrations — target database is up to date.");
        sb.AppendLine();

        // Change detection section
        sb.AppendLine("## Data Change Detection");
        if (diffs.Count == 0)
        {
            sb.AppendLine("? No data changes detected.");
        }
        else
        {
            sb.AppendLine($"?? **{diffs.Count}** table(s) have changes ({totalChanges} total rows affected).");
            sb.AppendLine();
            sb.AppendLine("| Table | Inserts | Updates | Deletes |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var d in diffs)
                sb.AppendLine($"| `{d.Table}` | {d.Inserts} | {d.Updates} | {d.Deletes} |");
        }
        sb.AppendLine();

        // Generated script section
        if (!string.IsNullOrWhiteSpace(generatedScript))
        {
            sb.AppendLine("## Generated Migration Script");
            sb.AppendLine($"?? `{Path.GetFileName(generatedScript)}`");
            sb.AppendLine();
            sb.AppendLine("> Script has been logged to `__MigrationLog` and is ready for review.");
        }

        return sb.ToString();
    }
}
