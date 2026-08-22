CREATE TABLE mail_rules (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    sender_contains TEXT NULL,
    subject_contains TEXT NULL,
    target_folder_id TEXT NOT NULL REFERENCES folders(id) ON DELETE CASCADE,
    sort_order INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX idx_mail_rules_account ON mail_rules(account_id);
