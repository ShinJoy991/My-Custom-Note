using Microsoft.Data.Sqlite;

namespace MyCustomNote.Data;

/// <summary>The database file is encrypted and the password given is not the one it was encrypted with (or none was given).</summary>
public sealed class WrongDatabasePasswordException : Exception
{
    public WrongDatabasePasswordException(Exception inner)
        : base("The database password is wrong, or the file is not a database.", inner)
    {
    }
}

/// <summary>
/// The SQLite file of the app: creates it, brings it up to the latest schema version (stored in
/// <c>PRAGMA user_version</c>) and hands out connections with foreign keys switched on.
/// The file can be encrypted as a whole with SQLCipher (AES-256): then every connection needs the password, and
/// without it the file is unreadable (notes, titles, tags, folders, everything).
/// </summary>
public sealed class NoteDatabase
{
    private const int NotADatabase = 26; // SQLITE_NOTADB: what SQLCipher reports for a wrong or missing key

    private readonly string _connectionString;

    static NoteDatabase()
    {
        // The SQLCipher build of SQLite is the one this app uses (it also opens plain, unencrypted files).
        SQLitePCL.Batteries_V2.Init();
    }

    public NoteDatabase(string path, string? password = null)
        : this(path, Schema.All, password)
    {
    }

    internal NoteDatabase(string path, IReadOnlyList<Migration> migrations, string? password = null)
    {
        Path = path;

        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = ConnectionString(path, password, SqliteOpenMode.ReadWriteCreate);

        try
        {
            Migrate(migrations);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == NotADatabase)
        {
            throw new WrongDatabasePasswordException(ex);
        }
    }

    public string Path { get; }

    // Pooling is off so that the file is released when a connection is disposed (tests delete it, files are
    // replaced when the encryption changes, and a desktop app with one user does not need a pool).
    private static string ConnectionString(string path, string? password, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            ForeignKeys = true,
            Pooling = false,
        };
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        return builder.ToString();
    }

    /// <summary>Opens a new connection. The caller disposes it.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Deleted and replaced text is overwritten with zeros in the file. Without this, the plain text of a note
        // that was just locked could stay readable inside the database file.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA secure_delete = ON";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    /// <summary>The schema version stored in the file.</summary>
    public int Version
    {
        get
        {
            using var connection = Open();
            return ReadVersion(connection);
        }
    }

    // ---------- encryption of the whole file ----------

    /// <summary>True if the file exists and does not start with the plain SQLite header (an encrypted file looks like random bytes).</summary>
    public static bool FileIsEncrypted(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] header = new byte[16];
        using var stream = File.OpenRead(path);
        int read = stream.Read(header, 0, header.Length);
        return read == header.Length && !header.AsSpan().SequenceEqual("SQLite format 3\0"u8);
    }

    /// <summary>True if the file can be opened with this password (an empty password means "not encrypted").</summary>
    public static bool CheckPassword(string path, string password)
    {
        try
        {
            using var connection = new SqliteConnection(ConnectionString(path, password, SqliteOpenMode.ReadWrite));
            connection.Open();
            Run(connection, "SELECT count(*) FROM sqlite_master");
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == NotADatabase)
        {
            return false;
        }
    }

    /// <summary>
    /// Encrypts an existing plain database file in place. The encrypted copy is made next to it and opened once with
    /// the password to prove it works; only then does it replace the original, and the original is overwritten with
    /// zeros before it is removed. Nothing is changed if any step fails. No connection to the file may be in use.
    /// (On an SSD, Windows may still keep old blocks somewhere; this cannot be controlled from here.)
    /// </summary>
    public static void Encrypt(string path, string password)
    {
        if (FileIsEncrypted(path))
        {
            throw new InvalidOperationException("The database is already encrypted.");
        }

        Export(path, source: null, target: password, replaceWiping: true);
    }

    /// <summary>Turns an encrypted file back into a plain one.</summary>
    public static void Decrypt(string path, string password)
    {
        if (!FileIsEncrypted(path))
        {
            throw new InvalidOperationException("The database is not encrypted.");
        }

        Export(path, source: password, target: null, replaceWiping: false);
    }

    /// <summary>Changes the password of an encrypted file. Throws <see cref="WrongDatabasePasswordException"/> if the old one is wrong.</summary>
    public static void ChangePassword(string path, string oldPassword, string newPassword)
    {
        try
        {
            using (var connection = new SqliteConnection(ConnectionString(path, oldPassword, SqliteOpenMode.ReadWrite)))
            {
                connection.Open();
                Run(connection, "SELECT count(*) FROM sqlite_master"); // proves the old password
                Run(connection, "PRAGMA rekey = " + Quote(newPassword));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == NotADatabase)
        {
            throw new WrongDatabasePasswordException(ex);
        }

        if (!CheckPassword(path, newPassword))
        {
            throw new IOException("The new password could not be verified after the change.");
        }
    }

    // Copies the whole database into a new file with (or without) a key, checks the copy, then swaps the files.
    private static void Export(string path, string? source, string? target, bool replaceWiping)
    {
        string temporary = path + ".converting";
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The database file does not exist.", path);
        }

        DeleteIfExists(temporary);

        try
        {
            try
            {
                // ReadWriteCreate on purpose: ATTACH opens the new file with the same flags as this connection, and
                // without the create flag SQLite cannot make it ("unable to open database", error 14). The source file
                // exists (checked above), so nothing is created here by accident.
                using (var connection = new SqliteConnection(ConnectionString(path, source, SqliteOpenMode.ReadWriteCreate)))
                {
                    connection.Open();
                    int version = Convert.ToInt32(Run(connection, "PRAGMA user_version"));
                    Run(connection, $"ATTACH DATABASE {Quote(temporary)} AS converted KEY {Quote(target ?? string.Empty)}");
                    Run(connection, "SELECT sqlcipher_export('converted')");
                    Run(connection, $"PRAGMA converted.user_version = {version}"); // the export does not copy it
                    Run(connection, "DETACH DATABASE converted");
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == NotADatabase)
            {
                throw new WrongDatabasePasswordException(ex);
            }

            // The copy must open with the new key (and the plain copy must really be plain) before it replaces anything.
            bool copyIsEncrypted = FileIsEncrypted(temporary);
            if (copyIsEncrypted != (target is not null) || !CheckPassword(temporary, target ?? string.Empty))
            {
                throw new IOException("The converted copy of the database could not be verified.");
            }

            if (replaceWiping)
            {
                Wipe(path);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            DeleteIfExists(temporary);
            throw;
        }
    }

    private static void Wipe(string path)
    {
        long left = new FileInfo(path).Length;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        byte[] zeros = new byte[81920];
        while (left > 0)
        {
            int count = (int)Math.Min(left, zeros.Length);
            stream.Write(zeros, 0, count);
            left -= count;
        }

        stream.Flush(flushToDisk: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // PRAGMA and ATTACH take no parameters, so text is written as an SQL string literal.
    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static object? Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    // ---------- schema ----------

    private void Migrate(IReadOnlyList<Migration> migrations)
    {
        using var connection = Open();
        int current = ReadVersion(connection);
        int latest = migrations.Count == 0 ? 0 : migrations.Max(m => m.Version);

        if (current > latest)
        {
            throw new InvalidOperationException(
                $"The database '{Path}' has schema version {current}, but this app only knows up to version {latest}. "
                + "It was probably written by a newer version of the app.");
        }

        foreach (var migration in migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            // One transaction per step: a failing statement leaves the database at the previous version.
            using var transaction = connection.BeginTransaction();
            foreach (string statement in migration.Statements)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            using var setVersion = connection.CreateCommand();
            setVersion.Transaction = transaction;
            setVersion.CommandText = $"PRAGMA user_version = {migration.Version}";
            setVersion.ExecuteNonQuery();

            transaction.Commit();
        }
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
