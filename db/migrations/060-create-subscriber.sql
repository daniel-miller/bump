-- 060: Per-tenant email subscribers (double opt-in).

CREATE TABLE IF NOT EXISTS subscriber
(
    subscriber_key      serial       PRIMARY KEY,
    subscriber_email    varchar(254) NOT NULL,

    tenant_key          int          NOT NULL REFERENCES tenant(tenant_key) ON DELETE CASCADE,

    confirm_token       bytea        NOT NULL,
    unsubscribe_token   bytea        NOT NULL,

    created_at          timestamptz  NOT NULL DEFAULT now(),
    confirmed_at        timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriber_tenant_email ON subscriber (tenant_key, lower(subscriber_email));
CREATE        INDEX IF NOT EXISTS ix_subscriber_confirmed    ON subscriber (tenant_key) WHERE confirmed_at IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriber_confirm_tok  ON subscriber (confirm_token);
CREATE UNIQUE INDEX IF NOT EXISTS ix_subscriber_unsub_tok    ON subscriber (unsubscribe_token);
