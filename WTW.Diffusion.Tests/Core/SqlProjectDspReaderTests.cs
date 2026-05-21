using System;
using System.IO;
using FluentAssertions;
using Moq;
using WTW.Diffusion.Core.Abstractions;
using WTW.Diffusion.Core.Helpers;
using WTW.Diffusion.Core.Services.Database;
using Xunit;

namespace WTW.Diffusion.Tests.Core;

public class SqlProjectDspReaderTests
{
    // ── ReadDsp ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReadDsp_ValidSqlproj_ReturnsDspValue()
    {
        var path = WriteTempSqlproj(
            "<Project><PropertyGroup>" +
            "<DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>" +
            "</PropertyGroup></Project>");

        SqlProjectDspReader.ReadDsp(path)
            .Should().Be("Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider");
    }

    [Fact]
    public void ReadDsp_NoDspElement_ReturnsNull()
    {
        var path = WriteTempSqlproj("<Project><PropertyGroup></PropertyGroup></Project>");
        SqlProjectDspReader.ReadDsp(path).Should().BeNull();
    }

    [Fact]
    public void ReadDsp_FileDoesNotExist_ReturnsNull()
    {
        SqlProjectDspReader.ReadDsp(@"C:\does\not\exist.sqlproj").Should().BeNull();
    }

    [Fact]
    public void ReadDsp_NullPath_ReturnsNull()
    {
        SqlProjectDspReader.ReadDsp(null).Should().BeNull();
    }

    [Fact]
    public void ReadDsp_EmptyPath_ReturnsNull()
    {
        SqlProjectDspReader.ReadDsp("   ").Should().BeNull();
    }

    // ── ParseMinimumMajorVersion ─────────────────────────────────────────────

    [Theory]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql90DatabaseSchemaProvider",  9)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql100DatabaseSchemaProvider", 10)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql110DatabaseSchemaProvider", 11)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql120DatabaseSchemaProvider", 12)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql130DatabaseSchemaProvider", 13)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql140DatabaseSchemaProvider", 14)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider", 15)]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.Sql160DatabaseSchemaProvider", 16)]
    public void ParseMinimumMajorVersion_KnownDsp_ReturnsCorrectVersion(string dsp, int expected)
    {
        SqlProjectDspReader.ParseMinimumMajorVersion(dsp).Should().Be(expected);
    }

    [Theory]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider")]
    [InlineData("Microsoft.Data.Tools.Schema.Sql.SqlAzureDatabaseSchemaProvider")]
    public void ParseMinimumMajorVersion_AzureDsp_ReturnsNull(string dsp)
    {
        SqlProjectDspReader.ParseMinimumMajorVersion(dsp).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomeUnknownProvider")]
    public void ParseMinimumMajorVersion_NullOrUnknown_ReturnsNull(string dsp)
    {
        SqlProjectDspReader.ParseMinimumMajorVersion(dsp).Should().BeNull();
    }

    // ── ValidateAndWarn ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateAndWarn_Override0_SkipsCheckEntirely()
    {
        var repo = new Mock<ISqlMigrationRepository>();
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, 0, null);

        repo.Verify(r => r.GetServerMajorVersion(It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.Log(It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndWarn_NullOverrideNullProjectPath_SkipsSilently()
    {
        var repo = new Mock<ISqlMigrationRepository>();
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, null, null);

        repo.Verify(r => r.GetServerMajorVersion(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndWarn_ServerMeetsMinimum_LogsInfo()
    {
        var repo = new Mock<ISqlMigrationRepository>();
        repo.Setup(r => r.GetServerMajorVersion("MyDb")).Returns(15);
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, 13, null);

        logger.Verify(l => l.Log(It.Is<string>(s => s.Contains("passed"))), Times.Once);
        logger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndWarn_ServerBelowMinimum_LogsWarning()
    {
        var repo = new Mock<ISqlMigrationRepository>();
        repo.Setup(r => r.GetServerMajorVersion("MyDb")).Returns(12);
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, 15, null);

        logger.Verify(l => l.LogWarning(It.Is<string>(s => s.Contains("12") && s.Contains("15"))), Times.Once);
        logger.Verify(l => l.Log(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndWarn_NullOverrideWithProjectFileDsp_DerivesMinFromDsp()
    {
        var path = WriteTempSqlproj(
            "<Project><PropertyGroup>" +
            "<DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>" +
            "</PropertyGroup></Project>");

        var repo = new Mock<ISqlMigrationRepository>();
        repo.Setup(r => r.GetServerMajorVersion("MyDb")).Returns(16);
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, null, path);

        repo.Verify(r => r.GetServerMajorVersion("MyDb"), Times.Once);
        logger.Verify(l => l.Log(It.Is<string>(s => s.Contains("passed"))), Times.Once);
    }

    [Fact]
    public void ValidateAndWarn_NullOverrideAzureDsp_SkipsSilently()
    {
        var path = WriteTempSqlproj(
            "<Project><PropertyGroup>" +
            "<DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>" +
            "</PropertyGroup></Project>");

        var repo = new Mock<ISqlMigrationRepository>();
        var logger = new Mock<ILogger>();

        SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, null, path);

        repo.Verify(r => r.GetServerMajorVersion(It.IsAny<string>()), Times.Never);
        logger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndWarn_RepositoryThrows_LogsWarningAndDoesNotRethrow()
    {
        var repo = new Mock<ISqlMigrationRepository>();
        repo.Setup(r => r.GetServerMajorVersion(It.IsAny<string>()))
            .Throws(new InvalidOperationException("connection failed"));
        var logger = new Mock<ILogger>();

        var act = () => SqlProjectDspReader.ValidateAndWarn("MyDb", repo.Object, logger.Object, 15, null);

        act.Should().NotThrow();
        logger.Verify(l => l.LogWarning(It.Is<string>(s => s.Contains("connection failed"))), Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string WriteTempSqlproj(string xmlContent)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlproj");
        File.WriteAllText(path, xmlContent);
        return path;
    }
}
