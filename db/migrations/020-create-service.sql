-- 020: Service (probed endpoint). Static config only — rolling probe
-- state lives in service_state to keep probe-driven UPDATEs off this row.

CREATE TABLE IF NOT EXISTS service
(
    service_key         serial        PRIMARY KEY,
    service_slug        varchar(60)   NOT NULL,
    service_name        varchar(100)  NOT NULL,
    service_url         varchar(2048) NOT NULL,
    service_paused      boolean       NOT NULL DEFAULT false,

    tenant_key          int           NOT NULL REFERENCES tenant(tenant_key)           ON DELETE CASCADE,

    environment_key     int           NOT NULL REFERENCES environment(environment_key) ON DELETE CASCADE,

    app_key             int           NOT NULL REFERENCES app(app_key)                 ON DELETE CASCADE,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_service_slug   ON service (service_slug);
CREATE        INDEX IF NOT EXISTS ix_service_active ON service (service_paused) WHERE service_paused = false;
CREATE        INDEX IF NOT EXISTS ix_service_tenant ON service (tenant_key);
CREATE        INDEX IF NOT EXISTS ix_service_env    ON service (environment_key);
CREATE        INDEX IF NOT EXISTS ix_service_app    ON service (app_key);
