-- 020: Service (probed endpoint). Static config only — rolling probe
-- state lives in service_state to keep probe-driven UPDATEs off this row.
--
-- app_key is nullable: a service is a URL bump probes, and not every probed
-- site has an application reporting versions to Bump (marketing sites,
-- third-party hosts). Deleting an app detaches its services rather than
-- deleting them, because the monitoring roster outlives any one reporter.
--
-- is_private hides a service from the admin services list by default.
-- It has no effect on probing or public boards.

CREATE TABLE IF NOT EXISTS service
(
    service_key         serial        PRIMARY KEY,
    service_handle        varchar(60)   NOT NULL,
    service_name        varchar(100)  NOT NULL,
    service_url         varchar(2048) NOT NULL,
    service_paused      boolean       NOT NULL DEFAULT false,
    is_private          boolean       NOT NULL DEFAULT false,

    owner_key           int           NOT NULL REFERENCES owner(owner_key)             ON DELETE CASCADE,

    environment_key     int           NOT NULL REFERENCES environment(environment_key) ON DELETE CASCADE,

    app_key             int                    REFERENCES app(app_key)                 ON DELETE SET NULL,

    created_at          timestamptz   NOT NULL DEFAULT now(),
    updated_at          timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_service_handle   ON service (service_handle);
CREATE        INDEX IF NOT EXISTS ix_service_active ON service (service_paused) WHERE service_paused = false;
CREATE        INDEX IF NOT EXISTS ix_service_owner  ON service (owner_key);
CREATE        INDEX IF NOT EXISTS ix_service_env    ON service (environment_key);
CREATE        INDEX IF NOT EXISTS ix_service_app    ON service (app_key);
