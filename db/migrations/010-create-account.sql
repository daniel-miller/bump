-- 010: Admin accounts (single-admin model, fixed seeded set).

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS account
(
    account_id         uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    account_email      varchar(254)  NOT NULL,
    account_full_name  varchar(200)  NOT NULL,
    account_timezone   varchar(64)   NOT NULL DEFAULT 'UTC',

    password_hash      bytea         NOT NULL,
    password_salt      bytea         NOT NULL,
    password_alg       varchar(32)   NOT NULL DEFAULT 'argon2id',

    totp_secret        bytea,
    totp_enabled       boolean       NOT NULL DEFAULT false,

    -- Authorization is driven from this column; SessionAuthHandler emits
    -- one Role claim per element. Default backfills existing/seeded rows
    -- to admin so the single-admin deployment keeps working.
    account_roles      text[]        NOT NULL DEFAULT ARRAY['admin']::text[],

    -- Per-account login throttling. failed_login_count increments on
    -- bad password / TOTP and resets on success; locked_until is set
    -- by AuthController when the count crosses MaxFailedLogins.
    failed_login_count integer       NOT NULL DEFAULT 0,
    locked_until       timestamptz,

    created_at         timestamptz   NOT NULL DEFAULT now(),
    updated_at         timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_account_email ON account (lower(account_email));
