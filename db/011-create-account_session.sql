-- 011: Server-side session registry. JWT carries (sub, sid); this table is
-- the revocation source of truth.

CREATE TABLE IF NOT EXISTS account_session
(
    session_id   uuid          PRIMARY KEY DEFAULT gen_random_uuid(),

    account_id   uuid          NOT NULL REFERENCES account(account_id) ON DELETE CASCADE,

    user_agent   varchar(500),
    user_ip      inet,

    bearer_token_hash   bytea         NOT NULL,

    created_at   timestamptz   NOT NULL DEFAULT now(),
    expires_at   timestamptz   NOT NULL,
    revoked_at   timestamptz
);

CREATE INDEX        IF NOT EXISTS ix_account_session_account ON account_session (account_id);
CREATE INDEX        IF NOT EXISTS ix_account_session_active  ON account_session (expires_at) WHERE revoked_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_account_session_token   ON account_session (bearer_token_hash);
