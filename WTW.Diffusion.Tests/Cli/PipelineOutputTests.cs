using FluentAssertions;
using WTW.Diffusion.Cli.Infrastructure;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Tests.Cli;

public class PipelineOutputTests
{
    // ?? No changes ???????????????????????????????????????????????????????????

    [Fact]
    public void BuildMarkdown_NoPendingNoChanges_ShowsUpToDateMessages()
    {
        var md = PipelineOutput.BuildMarkdown(0, new List<TableChangeSummary>(), null);

        md.Should().Contain("No pending migrations");
        md.Should().Contain("No data changes detected");
        md.Should().NotContain("Generated Migration Script");
    }

    // ?? Pending migrations ???????????????????????????????????????????????????

    [Fact]
    public void BuildMarkdown_WithPendingMigrations_ShowsAppliedCount()
    {
        var md = PipelineOutput.BuildMarkdown(3, new List<TableChangeSummary>(), null);

        md.Should().Contain("**3**");
        md.Should().Contain("applied successfully");
    }

    // ?? Data changes table ???????????????????????????????????????????????????

    [Fact]
    public void BuildMarkdown_WithChanges_RendersMarkdownTable()
    {
        var changes = new List<TableChangeSummary>
        {
            new() { Table = "dbo.Users",  Inserts = 5, Updates = 2, Deletes = 1 },
            new() { Table = "dbo.Orders", Inserts = 0, Updates = 3, Deletes = 0 }
        };

        var md = PipelineOutput.BuildMarkdown(0, changes, null);

        md.Should().Contain("| Table |");
        md.Should().Contain("`dbo.Users`");
        md.Should().Contain("| 5 |");
        md.Should().Contain("`dbo.Orders`");
        md.Should().Contain("| 3 |");
    }

    [Fact]
    public void BuildMarkdown_WithChanges_ShowsWarningEmoji()
    {
        var changes = new List<TableChangeSummary>
        {
            new() { Table = "dbo.Users", Inserts = 1, Updates = 0, Deletes = 0 }
        };

        var md = PipelineOutput.BuildMarkdown(0, changes, null);

        md.Should().Contain("[!WARNING]");
        md.Should().Contain("**1** table(s)");
        md.Should().Contain("1 total rows affected");
    }

    // ?? Generated script section ?????????????????????????????????????????????

    [Fact]
    public void BuildMarkdown_WithGeneratedScript_ShowsScriptSection()
    {
        var md = PipelineOutput.BuildMarkdown(
            0,
            new List<TableChangeSummary>(),
            @"C:\Migrations\001_Seed.sql");

        md.Should().Contain("Generated Migration Script");
        md.Should().Contain("`001_Seed.sql`");
        md.Should().Contain("__MigrationLog");
    }

    [Fact]
    public void BuildMarkdown_WithoutGeneratedScript_OmitsScriptSection()
    {
        var md = PipelineOutput.BuildMarkdown(0, new List<TableChangeSummary>(), null);

        md.Should().NotContain("Generated Migration Script");
    }

    // ?? Only tables with changes appear in the table ?????????????????????????

    [Fact]
    public void BuildMarkdown_MixedChanges_OnlyIncludesTablesWithDiffs()
    {
        var changes = new List<TableChangeSummary>
        {
            new() { Table = "dbo.NoChange", Inserts = 0, Updates = 0, Deletes = 0 },
            new() { Table = "dbo.Changed",  Inserts = 1, Updates = 0, Deletes = 0 }
        };

        var md = PipelineOutput.BuildMarkdown(0, changes, null);

        md.Should().Contain("`dbo.Changed`");
        md.Should().NotContain("`dbo.NoChange`");
    }
}
