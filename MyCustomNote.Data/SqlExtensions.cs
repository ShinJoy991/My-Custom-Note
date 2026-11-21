using Microsoft.Data.Sqlite;

namespace MyCustomNote.Data;

internal static class SqlExtensions
{
    /// <summary>A command with named parameters. A null value becomes SQL NULL; use 1 and 0 for booleans.</summary>
    public static SqliteCommand Sql(this SqliteConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static long? GetNullableInt64(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static bool GetFlag(this SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) != 0;

    public static int Flag(bool value) => value ? 1 : 0;
}
