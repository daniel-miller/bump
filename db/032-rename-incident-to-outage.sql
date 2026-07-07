-- 032: Rename "incident" terminology to "outage" across schema.
--
-- Wrapped in DO blocks so the migration is tolerant of three states:
--  a) Fresh DB: 030 created `outage` directly. `incident` never existed.
--     Everything here no-ops.
--  b) DB that ran the original 030/031 (`incident` tables) but never the
--     renamed 030. Plain rename path.
--  c) DB that ran BOTH the original 030 and the renamed 030 (because the
--     migrator treats the renamed file as a new entry). `incident` + empty
--     `outage` coexist. Drop the empty `outage` first, then rename.

DO $$
BEGIN
    IF to_regclass('public.incident') IS NOT NULL THEN
        IF to_regclass('public.outage') IS NOT NULL THEN
            DROP TABLE IF EXISTS outage_update CASCADE;
            DROP TABLE IF EXISTS outage CASCADE;
        END IF;

        ALTER TABLE incident RENAME TO outage;
        ALTER TABLE outage RENAME COLUMN incident_key    TO outage_key;
        ALTER TABLE outage RENAME COLUMN incident_title  TO outage_title;
        ALTER TABLE outage RENAME COLUMN incident_status TO outage_status;
        ALTER TABLE outage RENAME COLUMN incident_region TO outage_region;

        ALTER INDEX IF EXISTS ix_incident_started     RENAME TO ix_outage_started;
        ALTER INDEX IF EXISTS ix_incident_service     RENAME TO ix_outage_service;
        ALTER INDEX IF EXISTS ix_incident_status      RENAME TO ix_outage_status;
        ALTER INDEX IF EXISTS ix_incident_open_by_svc RENAME TO ix_outage_open_by_svc;
    END IF;
END $$;

DO $$
BEGIN
    IF to_regclass('public.incident_update') IS NOT NULL THEN
        ALTER TABLE incident_update RENAME TO outage_update;
        ALTER TABLE outage_update RENAME COLUMN incident_key TO outage_key;

        ALTER INDEX IF EXISTS ix_incident_update_incident RENAME TO ix_outage_update_outage;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_name = 'service_state'
           AND column_name = 'last_incident_at'
    ) THEN
        ALTER TABLE service_state RENAME COLUMN last_incident_at TO last_outage_at;
    END IF;
END $$;
