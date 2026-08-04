-- 025: Add service.site_id for databases created before the column joined
-- the 020 baseline.
--
-- 020-create-service.sql gained site_id on 2026-08-04, on the assumption
-- that no deployed database had run the fresh baseline yet. The live
-- database had: it was created from the rebuild commit a few hours earlier,
-- so the Migrator (which tracks applied files by name) will never re-run
-- the edited 020, and every service query 500'd with "column s.site_id
-- does not exist". Baseline edits are only safe before anything runs them;
-- after that, columns arrive like this one does.
--
-- IF NOT EXISTS makes this a no-op on databases whose 020 already carried
-- the column.

ALTER TABLE service
    ADD COLUMN IF NOT EXISTS site_id int;
