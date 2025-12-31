using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace data_foundry.Services
{
    public static class ServicesHelper
    {
        public static (string Database, string Server) ParseConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));

            var builder = new SqlConnectionStringBuilder(connectionString);
            return (builder.InitialCatalog, builder.DataSource);
        }
    }
}
