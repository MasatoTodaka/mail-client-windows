CREATE TABLE accounts (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    email_address TEXT NOT NULL,
    imap_host TEXT NOT NULL,
    imap_port INTEGER NOT NULL,
    imap_security INTEGER NOT NULL,
    imap_username TEXT NOT NULL,
    smtp_host TEXT NOT NULL,
    smtp_port INTEGER NOT NULL,
    smtp_security INTEGER NOT NULL,
    smtp_username TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE folders (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    imap_full_name TEXT NULL,
    display_name TEXT NOT NULL,
    special_use INTEGER NOT NULL DEFAULT 0,
    parent_folder_id TEXT NULL REFERENCES folders(id) ON DELETE SET NULL,
    uid_validity INTEGER NOT NULL DEFAULT 0,
    uid_next INTEGER NOT NULL DEFAULT 0,
    highest_mod_seq INTEGER NULL,
    unread_count INTEGER NOT NULL DEFAULT 0,
    total_count INTEGER NOT NULL DEFAULT 0,
    last_synced_at TEXT NULL
);
CREATE INDEX idx_folders_account ON folders(account_id);

CREATE TABLE folder_sync_state (
    folder_id TEXT PRIMARY KEY REFERENCES folders(id) ON DELETE CASCADE,
    uid_validity INTEGER NOT NULL DEFAULT 0,
    last_synced_uid INTEGER NOT NULL DEFAULT 0,
    oldest_synced_date TEXT NULL,
    initial_sync_complete INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE messages (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    folder_id TEXT NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
    uid INTEGER NOT NULL,
    message_id TEXT NULL,
    in_reply_to TEXT NULL,
    references_header TEXT NULL,
    subject TEXT NOT NULL,
    from_display TEXT NOT NULL,
    from_address TEXT NOT NULL,
    to_recipients TEXT NOT NULL,
    cc_recipients TEXT NULL,
    date TEXT NOT NULL,
    snippet TEXT NOT NULL DEFAULT '',
    is_read INTEGER NOT NULL DEFAULT 0,
    is_flagged INTEGER NOT NULL DEFAULT 0,
    is_answered INTEGER NOT NULL DEFAULT 0,
    is_draft INTEGER NOT NULL DEFAULT 0,
    has_attachments INTEGER NOT NULL DEFAULT 0,
    size INTEGER NOT NULL DEFAULT 0,
    is_body_downloaded INTEGER NOT NULL DEFAULT 0,
    body_text_path TEXT NULL,
    body_html_path TEXT NULL
);
CREATE UNIQUE INDEX idx_messages_folder_uid ON messages(folder_id, uid);
CREATE INDEX idx_messages_account ON messages(account_id);
CREATE INDEX idx_messages_message_id ON messages(message_id);

CREATE TABLE attachments (
    id TEXT PRIMARY KEY,
    message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size INTEGER NOT NULL DEFAULT 0,
    local_cache_path TEXT NULL,
    part_specifier TEXT NOT NULL
);
CREATE INDEX idx_attachments_message ON attachments(message_id);

CREATE TABLE outbox_actions (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    type INTEGER NOT NULL,
    message_id TEXT NULL,
    target_folder_id TEXT NULL,
    payload_json TEXT NULL,
    created_at TEXT NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT NULL
);
CREATE INDEX idx_outbox_account ON outbox_actions(account_id);
