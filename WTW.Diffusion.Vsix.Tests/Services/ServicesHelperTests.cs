using System;
using FluentAssertions;
using Xunit;
using data_foundry.Services;

namespace WTW.Diffusion.Vsix.Tests.Services
{
    public class ServicesHelperTests
    {
        // ?? Happy path ????????????????????????????????????????????????????????

        [Fact]
        public void ParseConnectionString_StandardSqlServer_ReturnsDatabaseAndServer()
        {
            var (db, server) = ServicesHelper.ParseConnectionString(
                "Server=myserver;Database=MyDb;Integrated Security=true;");

            db.Should().Be("MyDb");
            server.Should().Be("myserver");
        }

        [Fact]
        public void ParseConnectionString_LocalInstance_ReturnsCorrectParts()
        {
            var (db, server) = ServicesHelper.ParseConnectionString(
                @"Server=.\SQLEXPRESS;Database=AppDb;Integrated Security=true;");

            db.Should().Be("AppDb");
            server.Should().Be(@".\SQLEXPRESS");
        }

        [Fact]
        public void ParseConnectionString_AzureSql_ReturnsCorrectParts()
        {
            var (db, server) = ServicesHelper.ParseConnectionString(
                "Server=myserver.database.windows.net;Database=ProdDb;Integrated Security=false;");

            db.Should().Be("ProdDb");
            server.Should().Be("myserver.database.windows.net");
        }

        [Fact]
        public void ParseConnectionString_InitialCatalogKeyword_ReturnsDatabase()
        {
            var (db, _) = ServicesHelper.ParseConnectionString(
                "Data Source=srv;Initial Catalog=CatalogDb;Integrated Security=true;");

            db.Should().Be("CatalogDb");
        }

        // ?? Guard clauses ?????????????????????????????????????????????????????

        [Fact]
        public void ParseConnectionString_Null_ThrowsArgumentException()
        {
            Action act = () => ServicesHelper.ParseConnectionString(null);
            act.Should().Throw<ArgumentException>().WithMessage("*null or empty*");
        }

        [Fact]
        public void ParseConnectionString_Empty_ThrowsArgumentException()
        {
            Action act = () => ServicesHelper.ParseConnectionString(string.Empty);
            act.Should().Throw<ArgumentException>().WithMessage("*null or empty*");
        }

        [Fact]
        public void ParseConnectionString_Whitespace_ThrowsArgumentException()
        {
            Action act = () => ServicesHelper.ParseConnectionString("   ");
            act.Should().Throw<ArgumentException>().WithMessage("*null or empty*");
        }
    }
}
