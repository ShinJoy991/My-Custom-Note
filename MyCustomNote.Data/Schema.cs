namespace MyCustomNote.Data;

/// <summary>One step of the database schema: the statements that bring it from <c>Version - 1</c> to <c>Version</c>.</summary>
internal sealed record Migration(int Version, IReadOnlyList<string> Statements);

/// <summary>
/// The Windows database schema. It mirrors the Android (Room, version 4) tables and columns so notes map one to
/// one, plus <c>paragraphsJson</c> for the heading and list kinds (Android ignores it). The two databases are never
/// shared (JSON import and export is the bridge), so this schema has its own version numbers, starting at 1.
/// New versions are only ever added at the end; a released migration is never edited.
/// </summary>
internal static class Schema
{
    public static IReadOnlyList<Migration> All { get; } =
    [
        new Migration(1,
        [
            """
            CREATE TABLE notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL DEFAULT '',
                body TEXT NOT NULL DEFAULT '',
                folderId INTEGER NULL,
                colorTag TEXT NULL,
                isPinned INTEGER NOT NULL DEFAULT 0,
                isArchived INTEGER NOT NULL DEFAULT 0,
                isTrashed INTEGER NOT NULL DEFAULT 0,
                createdAt INTEGER NOT NULL,
                modifiedAt INTEGER NOT NULL,
                lastOpenedAt INTEGER NULL,
                trashedAt INTEGER NULL,
                sortOrder INTEGER NOT NULL DEFAULT 0,
                isLocked INTEGER NOT NULL DEFAULT 0,
                isHidden INTEGER NOT NULL DEFAULT 0,
                bodyStylesJson TEXT NOT NULL DEFAULT '[]',
                titleStylesJson TEXT NOT NULL DEFAULT '[]',
                paragraphsJson TEXT NOT NULL DEFAULT '[]'
            )
            """,
            "CREATE INDEX index_notes_folderId ON notes (folderId)",
            """
            CREATE TABLE folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parentFolderId INTEGER NULL,
                sortOrder INTEGER NOT NULL DEFAULT 0
            )
            """,
            """
            CREATE TABLE tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL
            )
            """,
            "CREATE UNIQUE INDEX index_tags_name ON tags (name)",
            """
            CREATE TABLE note_tag_cross_ref (
                noteId INTEGER NOT NULL,
                tagId INTEGER NOT NULL,
                PRIMARY KEY (noteId, tagId),
                FOREIGN KEY (noteId) REFERENCES notes (id) ON DELETE CASCADE,
                FOREIGN KEY (tagId) REFERENCES tags (id) ON DELETE CASCADE
            )
            """,
            "CREATE INDEX index_note_tag_cross_ref_tagId ON note_tag_cross_ref (tagId)",
            """
            CREATE TABLE checklist_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                noteId INTEGER NOT NULL,
                text TEXT NOT NULL,
                isChecked INTEGER NOT NULL DEFAULT 0,
                position INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (noteId) REFERENCES notes (id) ON DELETE CASCADE
            )
            """,
            "CREATE INDEX index_checklist_items_noteId ON checklist_items (noteId)",
        ]),
    ];
}
