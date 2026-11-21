using Microsoft.Data.Sqlite;

namespace MyCustomNote.Data.Tests;

/// <summary>A database file in the temp folder that is deleted when the test ends.</summary>
internal sealed class TempDatabasePath : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mycustomnote-tests", Guid.NewGuid() + ".db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            if (File.Exists(Path + suffix))
            {
                File.Delete(Path + suffix);
            }
        }
    }
}

internal static class SqlHelper
{
    public static void Run(this SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static long Count(this SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public static List<string> Column(this SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>Inserts a note with only the required columns and returns its id.</summary>
    public static long InsertNote(this SqliteConnection connection, string title = "t")
    {
        connection.Run($"INSERT INTO notes (title, createdAt, modifiedAt) VALUES ('{title}', 1, 1)");
        return connection.Count("SELECT last_insert_rowid()");
    }
}
