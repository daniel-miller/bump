-- 022: Per-service per-day metrics. 30-day retention enforced by prober.

CREATE TABLE IF NOT EXISTS service_daily
(
    service_key         int     NOT NULL REFERENCES service(service_key) ON DELETE CASCADE,
    day                 date    NOT NULL,
    probes              int     NOT NULL DEFAULT 0,
    operational_count   int     NOT NULL DEFAULT 0,
    latency_sum_ms      bigint  NOT NULL DEFAULT 0,
    PRIMARY KEY (service_key, day)
);

CREATE INDEX IF NOT EXISTS ix_service_daily_day ON service_daily (day);
