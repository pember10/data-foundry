using FluentAssertions;
using WTW.Diffusion.Core.Helpers;

namespace WTW.Diffusion.Tests.Core;

public class PathHelperTests
{
    // ?? SanitizePathComponent ????????????????????????????????????????????????

    [Fact]
    public void SanitizePathComponent_CleanName_ReturnsUnchanged()
    {
        PathHelper.SanitizePathComponent("MyMigrations")
            .Should().Be("MyMigrations");
    }

    [Theory]
    [InlineData("My:File", "My_File")]
    [InlineData("My/File", "My_File")]
    [InlineData("My\\File", "My_File")]
    [InlineData("My?File", "My_File")]
    [InlineData("My*File", "My_File")]
    public void SanitizePathComponent_InvalidChars_ReplacedWithUnderscore(string input, string expected)
    {
        PathHelper.SanitizePathComponent(input)
            .Should().Be(expected);
    }

    [Fact]
    public void SanitizePathComponent_CustomReplacement_UsesSuppliedChar()
    {
        PathHelper.SanitizePathComponent("My:File", '-')
            .Should().Be("My-File");
    }

    [Fact]
    public void SanitizePathComponent_NullInput_ReturnsNull()
    {
        PathHelper.SanitizePathComponent(null)
            .Should().BeNull();
    }

    [Fact]
    public void SanitizePathComponent_WhitespaceOnly_ReturnsWhitespace()
    {
        PathHelper.SanitizePathComponent("   ")
            .Should().Be("   ");
    }

    // ?? IsValidPath ??????????????????????????????????????????????????????????

    [Fact]
    public void IsValidPath_AbsoluteWindowsPath_ReturnsTrue()
    {
        PathHelper.IsValidPath(@"C:\source\data-foundry\Migrations")
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidPath_NullOrEmpty_ReturnsFalse()
    {
        PathHelper.IsValidPath(null).Should().BeFalse();
        PathHelper.IsValidPath("").Should().BeFalse();
        PathHelper.IsValidPath("   ").Should().BeFalse();
    }

    [Fact]
    public void IsValidPath_PathWithNullChar_ReturnsFalse()
    {
        PathHelper.IsValidPath("C:\\bad\0path")
            .Should().BeFalse();
    }

    // ?? SafeCombine ??????????????????????????????????????????????????????????

    [Fact]
    public void SafeCombine_TwoValidParts_CombinesThem()
    {
        PathHelper.SafeCombine(@"C:\base", "sub")
            .Should().Be(@"C:\base\sub");
    }

    [Fact]
    public void SafeCombine_NullPart_ReturnsNull()
    {
        PathHelper.SafeCombine(null, "sub").Should().BeNull();
        PathHelper.SafeCombine(@"C:\base", null).Should().BeNull();
    }
}
