using Microsoft.Data.Sqlite;

namespace DapperVsEfApi.Data;

// Dapper doesn't own a connection the way a DbContext does - it just needs
// an open IDbConnection to run SQL against. Same SQLite file EF Core uses,
// reached through the plain ADO.NET provider instead of EF Core's stack.
public class SqliteConnectionFactory(string connectionString)
{
    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
