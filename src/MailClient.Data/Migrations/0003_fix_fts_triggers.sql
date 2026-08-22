-- 0002's UPDATE/DELETE triggers used FTS5's INSERT-based special 'delete' command
-- (`INSERT INTO messages_fts(messages_fts, rowid, ...) VALUES('delete', ...)`), which is only
-- valid for external-content FTS5 tables. messages_fts is a standalone (non-external-content)
-- table, which instead supports plain DELETE/UPDATE against it directly — using the special
-- command raised "SQLite Error 1: 'SQL logic error'" on every update or delete of a message.
DROP TRIGGER messages_fts_ad;
DROP TRIGGER messages_fts_au;

CREATE TRIGGER messages_fts_ad AFTER DELETE ON messages BEGIN
    DELETE FROM messages_fts WHERE rowid = old.rowid;
END;

CREATE TRIGGER messages_fts_au AFTER UPDATE ON messages BEGIN
    UPDATE messages_fts SET
        message_id = new.id,
        subject = new.subject,
        from_display = new.from_display,
        from_address = new.from_address,
        snippet = new.snippet
    WHERE rowid = old.rowid;
END;
