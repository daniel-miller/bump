-- 031: Append-only outage timeline.

CREATE TABLE IF NOT EXISTS outage_update
(
    update_key         bigserial     PRIMARY KEY,
    update_message     varchar(2000) NOT NULL,

    outage_key         int           NOT NULL REFERENCES outage(outage_key) ON DELETE CASCADE,

    update_status   varchar(16)   NOT NULL
        CHECK (update_status IN ('investigating','identified','monitoring','resolved')),

    published          boolean       NOT NULL DEFAULT true,

    created_by         uuid          REFERENCES account(account_id) ON DELETE SET NULL,
    created_at         timestamptz   NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_outage_update_outage ON outage_update (outage_key, created_at);
