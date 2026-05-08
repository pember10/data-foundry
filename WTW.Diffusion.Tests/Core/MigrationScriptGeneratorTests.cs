using System.Data;
using FluentAssertions;
using Moq;
using WTW.Diffusion.Core.Services.Database;
using WTW.Diffusion.Core.Services.Migration;

namespace WTW.Diffusion.Tests.Core;

/// <summary>
/// Tests for MigrationScriptGenerator SQL output.
/// SqlMigrationRepository is mocked — no database required.
/// </summary>
public class MigrationScriptGeneratorTests
{
    private const string TargetDb = "TargetDb";
    private const string ShadowDb = "ShadowDb";
    private const string Table    = "Users";

    private readonly Mock<ISqlMigrationRepository> _repoMock;
    private readonly MigrationScriptGenerator _generator;
    private readonly string _outputDir;

    public MigrationScriptGeneratorTests()
    {
        _repoMock  = new Mock<ISqlMigrationRepository>();
        _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_outputDir);

        var scriptManager = new Mock<IMigrationScriptManager>();
        _generator = new MigrationScriptGenerator(_repoMock.Object);
    }

    // ?? Helpers ??????????????????????????????????????????????????????????????

    private static DataTable MakeColumns(params string[] names)
    {
        var dt = new DataTable();
        dt.Columns.Add("ColumnName");
        dt.Columns.Add("TypeName");
        foreach (var n in names)
        {
            var row = dt.NewRow();
            row["ColumnName"] = n;
            row["TypeName"]   = "nvarchar";
            dt.Rows.Add(row);
        }
        return dt;
    }

    private static DataTable MakeRows(string[] columns, params object[][] rows)
    {
        var dt = new DataTable();
        foreach (var c in columns) dt.Columns.Add(c);
        foreach (var r in rows)
        {
            var row = dt.NewRow();
            for (int i = 0; i < r.Length; i++) row[i] = r[i] ?? DBNull.Value;
            dt.Rows.Add(row);
        }
        return dt;
    }

    // ?? INSERT generation ????????????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_NewRowInTarget_ProducesInsert()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table))
                 .Returns(new List<string> { "Id" });
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table))
                 .Returns(MakeColumns("Id", "Name"));
        _repoMock.Setup(r => r.GetTableData(TargetDb, Table))
                 .Returns(MakeRows(["Id", "Name"], [1, "Alice"]));
        _repoMock.Setup(r => r.GetTableData(ShadowDb, Table))
                 .Returns(MakeRows(["Id", "Name"])); // empty shadow

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_Insert");

        var sql = File.ReadAllText(path);
        sql.Should().Contain("INSERT INTO");
        sql.Should().Contain("[Id]");
        sql.Should().Contain("[Name]");
        sql.Should().Contain("'Alice'");
        sql.Should().Contain("1");
    }

    // ?? DELETE generation ????????????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_RowRemovedFromTarget_ProducesDelete()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table))
                 .Returns(new List<string> { "Id" });
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table))
                 .Returns(MakeColumns("Id", "Name"));
        _repoMock.Setup(r => r.GetTableData(TargetDb, Table))
                 .Returns(MakeRows(["Id", "Name"])); // empty target
        _repoMock.Setup(r => r.GetTableData(ShadowDb, Table))
                 .Returns(MakeRows(["Id", "Name"], [42, "Bob"])); // shadow has row

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_Delete");

        var sql = File.ReadAllText(path);
        sql.Should().Contain("DELETE FROM");
        sql.Should().Contain("42");
    }

    // ?? UPDATE generation ????????????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_ChangedRow_ProducesUpdate()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table))
                 .Returns(new List<string> { "Id" });
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table))
                 .Returns(MakeColumns("Id", "Name"));
        _repoMock.Setup(r => r.GetTableData(TargetDb, Table))
                 .Returns(MakeRows(["Id", "Name"], [1, "Alice Updated"]));
        _repoMock.Setup(r => r.GetTableData(ShadowDb, Table))
                 .Returns(MakeRows(["Id", "Name"], [1, "Alice"]));

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_Update");

        var sql = File.ReadAllText(path);
        sql.Should().Contain("UPDATE");
        sql.Should().Contain("'Alice Updated'");
    }

    // ?? No primary key ???????????????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_NoPrimaryKey_EmitsSkipComment()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table))
                 .Returns(new List<string>());
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table))
                 .Returns(MakeColumns("Name"));
        _repoMock.Setup(r => r.GetTableData(It.IsAny<string>(), Table))
                 .Returns(MakeRows(["Name"]));

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_NoPk");

        File.ReadAllText(path).Should().Contain("Skipped");
    }

    // ?? Migration ID header ??????????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_Always_EmitsMigrationIdComment()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table)).Returns(new List<string>());
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table)).Returns(MakeColumns("Id"));
        _repoMock.Setup(r => r.GetTableData(It.IsAny<string>(), Table)).Returns(MakeRows(["Id"]));

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_Header");

        File.ReadAllText(path).Should().MatchRegex(@"-- <Migration ID=""[0-9a-f\-]+"" />");
    }

    // ?? SQL literal formatting ???????????????????????????????????????????????

    [Fact]
    public void GenerateMigrationScript_StringWithSingleQuote_EscapesIt()
    {
        _repoMock.Setup(r => r.GetPrimaryKeyColumns(TargetDb, Table))
                 .Returns(new List<string> { "Id" });
        _repoMock.Setup(r => r.GetColumnMetadata(TargetDb, Table))
                 .Returns(MakeColumns("Id", "Name"));
        _repoMock.Setup(r => r.GetTableData(TargetDb, Table))
                 .Returns(MakeRows(["Id", "Name"], [1, "O'Brien"]));
        _repoMock.Setup(r => r.GetTableData(ShadowDb, Table))
                 .Returns(MakeRows(["Id", "Name"]));

        var path = _generator.GenerateMigrationScript(
            TargetDb, ShadowDb, new List<string> { Table }, _outputDir, "Test_Quote");

        File.ReadAllText(path).Should().Contain("'O''Brien'");
    }

    public void Dispose() => Directory.Delete(_outputDir, recursive: true);
}
