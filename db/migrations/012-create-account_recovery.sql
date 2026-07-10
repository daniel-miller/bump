-- 012: 2FA recovery codes (one-time use, SHA-256 stored).

CREATE TABLE IF NOT EXISTS account_recovery
(
    recovery_key    bigserial     PRIMARY KEY,
    recovery_hash   bytea         NOT NULL,

    account_id  uuid          NOT NULL REFERENCES account(account_id) ON DELETE CASCADE,

    created_at  timestamptz   NOT NULL DEFAULT now(),
    used_at     timestamptz
);

CREATE INDEX IF NOT EXISTS ix_account_recovery_account ON account_recovery (account_id);
