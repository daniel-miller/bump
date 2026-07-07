-- 013: Password change tokens (short-lived, one-time use).

CREATE TABLE IF NOT EXISTS account_password_change
(
    change_key   bigserial     PRIMARY KEY,
    change_hash  bytea         NOT NULL,

    account_id  uuid          NOT NULL REFERENCES account(account_id) ON DELETE CASCADE,
    used_at     timestamptz,

    created_at  timestamptz   NOT NULL DEFAULT now(),
    expires_at  timestamptz   NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_account_password_change_hash ON account_password_change (change_hash);
