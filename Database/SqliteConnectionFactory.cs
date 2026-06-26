using Microsoft.Data.Sqlite;
using MiniERP2.Config;

namespace MiniERP2.Database;

public static class SqliteConnectionFactory
{
    public static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}");
        connection.Open();
        DbSchema.EnsureCreated(connection);
        return connection;
    }
}
