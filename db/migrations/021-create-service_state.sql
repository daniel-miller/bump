-- 021: Per-service rolling probe state. Re-written every probe cycle, so
-- it lives apart from the static service row to avoid bloat on config reads.
-- history_json is a JSON string array of "operational" | "degraded" | "down"
-- of length Bump:Monitors:HistoryBars (default 60), front-padded by the prober.

CREATE TABLE IF NOT EXISTS service_state
(
    service_key       int           PRIMARY KEY REFERENCES service(service_key) ON DELETE CASCADE,
    history_json      jsonb         NOT NULL DEFAULT '[]'::jsonb,
    latency_ms        int           NOT NULL DEFAULT 0,
    uptime_pct        numeric(5,2)  NOT NULL DEFAULT 100.00,
    last_status       varchar(16)   NOT NULL DEFAULT 'operational',
    last_check_at     timestamptz,
    last_outage_at    timestamptz
);
