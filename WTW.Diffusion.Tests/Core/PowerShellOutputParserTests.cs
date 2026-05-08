using FluentAssertions;
using WTW.Diffusion.Core.Services.Migration;

namespace WTW.Diffusion.Tests.Core;

public class PowerShellOutputParserTests
{
    // ?? ParseChanges ?????????????????????????????????????????????????????????

    [Fact]
    public void ParseChanges_WellFormedOutput_ReturnsCorrectSummaries()
    {
        var output = new List<string>
        {
            "Table      Inserts Updates Deletes",
            "-----      ------- ------- -------",
            "dbo.Users  5       2       1",
            "dbo.Orders 0       3       0"
        };

        var result = PowerShellOutputParser.ParseChanges(output);

        result.Should().HaveCount(2);
        result[0].Table.Should().Be("dbo.Users");
        result[0].Inserts.Should().Be(5);
        result[0].Updates.Should().Be(2);
        result[0].Deletes.Should().Be(1);
        result[1].Table.Should().Be("dbo.Orders");
        result[1].Updates.Should().Be(3);
    }

    [Fact]
    public void ParseChanges_EmptyOutput_ReturnsEmptyList()
    {
        PowerShellOutputParser.ParseChanges(new List<string>())
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_NoHeaderLine_ReturnsEmptyList()
    {
        var output = new List<string> { "dbo.Users  5  2  1" };

        PowerShellOutputParser.ParseChanges(output)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_MalformedDataRow_IsSkipped()
    {
        var output = new List<string>
        {
            "Table  Inserts Updates Deletes",
            "-----  ------- ------- -------",
            "bad",                          // too few parts
            "dbo.Roles  1  0  0"
        };

        var result = PowerShellOutputParser.ParseChanges(output);

        result.Should().HaveCount(1);
        result[0].Table.Should().Be("dbo.Roles");
    }

    // ?? ParsePendingMigrations ???????????????????????????????????????????????

    [Fact]
    public void ParsePendingMigrations_WellFormedOutput_ReturnsMigrations()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var output = new List<string>
        {
            "Pending migrations:",
            $"  -> 001_AddUsers.sql [{guid1}]",
            $"  -> 002_AddOrders.sql [{guid2}]",
            ""
        };

        var result = PowerShellOutputParser.ParsePendingMigrations(output);

        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("001_AddUsers.sql");
        result[0].Id.Should().Be(guid1);
        result[1].FileName.Should().Be("002_AddOrders.sql");
        result[1].Id.Should().Be(guid2);
    }

    [Fact]
    public void ParsePendingMigrations_BlankLineCutsSection()
    {
        var guid = Guid.NewGuid();
        var output = new List<string>
        {
            "Pending migrations:",
            $"  -> 001_AddUsers.sql [{guid}]",
            "",                             // blank line ends section
            $"  -> 002_AddOrders.sql [{Guid.NewGuid()}]"  // should NOT be parsed
        };

        PowerShellOutputParser.ParsePendingMigrations(output)
            .Should().HaveCount(1);
    }

    [Fact]
    public void ParsePendingMigrations_NoPendingSection_ReturnsEmpty()
    {
        var output = new List<string> { "No pending migrations found." };

        PowerShellOutputParser.ParsePendingMigrations(output)
            .Should().BeEmpty();
    }

    // ?? ParseGeneratedScriptPath ?????????????????????????????????????????????

    [Fact]
    public void ParseGeneratedScriptPath_ValidLine_ReturnsPath()
    {
        var output = new List<string>
        {
            "Detecting changes...",
            "Generated migration script: C:\\Migrations\\001_Seed.sql"
        };

        PowerShellOutputParser.ParseGeneratedScriptPath(output)
            .Should().Be("C:\\Migrations\\001_Seed.sql");
    }

    [Fact]
    public void ParseGeneratedScriptPath_NotPresent_ReturnsNull()
    {
        PowerShellOutputParser.ParseGeneratedScriptPath(new List<string> { "Done." })
            .Should().BeNull();
    }
}
