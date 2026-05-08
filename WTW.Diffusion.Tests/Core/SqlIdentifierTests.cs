using FluentAssertions;
using WTW.Diffusion.Core.Services.Database;

namespace WTW.Diffusion.Tests.Core;

/// <summary>
/// Tests for the SQL identifier validation and quoting logic.
/// These are critical security boundaries — they prevent SQL injection
/// via database/table/column names.
/// </summary>
public class SqlIdentifierTests
{
    // ?? QuoteIdentifier ??????????????????????????????????????????????????????

    [Fact]
    public void QuoteIdentifier_PlainName_WrapsInBrackets()
    {
        ChangeDetectionService.QuoteIdentifier("MyTable")
            .Should().Be("[MyTable]");
    }

    [Fact]
    public void QuoteIdentifier_NameContainingClosingBracket_EscapesIt()
    {
        ChangeDetectionService.QuoteIdentifier("Table]Name")
            .Should().Be("[Table]]Name]");
    }

    [Fact]
    public void QuoteIdentifier_NameContainingMultipleBrackets_EscapesAll()
    {
        ChangeDetectionService.QuoteIdentifier("A]B]C")
            .Should().Be("[A]]B]]C]");
    }

    [Fact]
    public void QuoteIdentifier_EmptyString_ReturnsEmptyBrackets()
    {
        ChangeDetectionService.QuoteIdentifier("")
            .Should().Be("[]");
    }

    // ?? ValidateIdentifier — table/column names ??????????????????????????????

    [Fact]
    public void ValidateIdentifier_ValidTableName_DoesNotThrow()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier("MyTable", "table");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateIdentifier_NameWithUnderscore_DoesNotThrow()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier("My_Table_123", "table");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateIdentifier_EmptyName_ThrowsArgumentException()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier("", "table");
        act.Should().Throw<ArgumentException>().WithMessage("*null or empty*");
    }

    [Fact]
    public void ValidateIdentifier_NullName_ThrowsArgumentException()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier(null, "table");
        act.Should().Throw<ArgumentException>().WithMessage("*null or empty*");
    }

    [Fact]
    public void ValidateIdentifier_NameExceeding128Chars_ThrowsArgumentException()
    {
        var longName = new string('A', 129);
        Action act = () => ChangeDetectionService.ValidateIdentifier(longName, "table");
        act.Should().Throw<ArgumentException>().WithMessage("*128*");
    }

    [Fact]
    public void ValidateIdentifier_TableNameWithSpecialChars_ThrowsArgumentException()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier("My-Table", "table");
        act.Should().Throw<ArgumentException>().WithMessage("*invalid characters*");
    }

    // ?? ValidateIdentifier — database names ??????????????????????????????????

    [Theory]
    [InlineData("'; DROP TABLE Users; --")]
    [InlineData("db\"name")]
    [InlineData("db;name")]
    [InlineData("db--name")]
    [InlineData("db/*name")]
    [InlineData("xp_cmdshell")]
    [InlineData("sp_execute")]
    public void ValidateIdentifier_DatabaseNameWithDangerousChars_ThrowsArgumentException(string name)
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier(name, "targetDatabase");
        act.Should().Throw<ArgumentException>().WithMessage("*invalid or dangerous*");
    }

    [Fact]
    public void ValidateIdentifier_ValidDatabaseName_DoesNotThrow()
    {
        Action act = () => ChangeDetectionService.ValidateIdentifier("MyApp_Dev", "targetDatabase");
        act.Should().NotThrow();
    }
}
