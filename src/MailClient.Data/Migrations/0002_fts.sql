-- Standalone (non-external-content) FTS5 index: rows are duplicated here rather than
-- looked up from `messages` on the fly, keyed by the same implicit rowid as `messages`.
-- Simpler to reason about than external-content mode and cheap at this row size.
CREATE VIRTUAL TABLE messages_fts USING fts5(
    message_id UNINDEXED,
    subject,
    from_display,
    from_address,
    snippet,
    tokenize = 'unicode61'
);

CREATE TRIGGER messages_fts_ai AFTER INSERT ON messages BEGIN
    INSERT INTO messages_fts(rowid, message_id, subject, from_display, from_address, snippet)
    VALUES (new.rowid, new.id, new.subject, new.from_display, new.from_address, new.snippet);
END;

CREATE TRIGGER messages_fts_ad AFTER DELETE ON messages BEGIN
    INSERT INTO messages_fts(messages_fts, rowid, message_id, subject, from_display, from_address, snippet)
    VALUES ('delete', old.rowid, old.id, old.subject, old.from_display, old.from_address, old.snippet);
END;

CREATE TRIGGER messages_fts_au AFTER UPDATE ON messages BEGIN
    INSERT INTO messages_fts(messages_fts, rowid, message_id, subject, from_display, from_address, snippet)
    VALUES ('delete', old.rowid, old.id, old.subject, old.from_display, old.from_address, old.snippet);
    INSERT INTO messages_fts(rowid, message_id, subject, from_display, from_address, snippet)
    VALUES (new.rowid, new.id, new.subject, new.from_display, new.from_address, new.snippet);
END;
