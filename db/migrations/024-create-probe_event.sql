-- 024: Per-probe event log. Replaces service_daily so day-bucket aggregation
-- can run in arbitrary IANA timezones via (checked_at AT TIME ZONE @tz).
-- Prober writes one row per tick (~60s default).

CREATE TABLE IF NOT EXISTS probe_event
(
    probe_event_key  bigserial    PRIMARY KEY,
    service_key      int          NOT NULL REFERENCES service(service_key) ON DELETE CASCADE,
    checked_at       timestamptz  NOT NULL DEFAULT now(),
    is_operational   boolean      NOT NULL,
    latency_ms       int          NOT NULL,
    probe_status     varchar(16)  NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_probe_event_service_checked
    ON probe_event (service_key, checked_at DESC);
