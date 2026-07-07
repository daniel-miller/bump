-- 030: Outages. service_key null is allowed for system-wide outages.

CREATE TABLE IF NOT EXISTS outage
(
    outage_key      serial        PRIMARY KEY,
    outage_title    varchar(200)  NOT NULL,
    outage_status   varchar(16)   NOT NULL DEFAULT 'investigating'
        CHECK (outage_status IN ('investigating','identified','monitoring','resolved')),
    outage_region   varchar(100),

    service_key     int           REFERENCES service(service_key) ON DELETE SET NULL,

    root_cause      text,

    auto_created    boolean       NOT NULL DEFAULT false,

    created_by      uuid          REFERENCES account(account_id) ON DELETE SET NULL,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    started_at      timestamptz   NOT NULL DEFAULT now(),
    resolved_at     timestamptz,
    updated_at      timestamptz
);

CREATE INDEX IF NOT EXISTS ix_outage_started     ON outage (started_at DESC);
CREATE INDEX IF NOT EXISTS ix_outage_service     ON outage (service_key);
CREATE INDEX IF NOT EXISTS ix_outage_status      ON outage (outage_status);
CREATE INDEX IF NOT EXISTS ix_outage_open_by_svc ON outage (service_key) WHERE outage_status <> 'resolved';
