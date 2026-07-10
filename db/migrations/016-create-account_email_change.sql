-- 016: Pending-email-change tokens.
--
-- Email changes are split into two steps: a request stores the
-- candidate address plus a one-time token, and a confirmation step
-- proves possession of the new mailbox before account.account_email
-- is overwritten. Token is hashed at rest; the raw value lives only
-- in the confirmation email.

CREATE TABLE IF NOT EXISTS account_email_change
(
    change_key  bigserial    PRIMARY KEY,
    account_id uuid         NOT NULL REFERENCES account(account_id) ON DELETE CASCADE,
    new_email  varchar(254) NOT NULL,
    change_hash bytea        NOT NULL,
    expires_at timestamptz  NOT NULL,
    used_at    timestamptz,
    created_at timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_account_email_change_hash    ON account_email_change (change_hash);
CREATE INDEX IF NOT EXISTS ix_account_email_change_account ON account_email_change (account_id);
