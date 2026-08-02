-- 100: Align the environment roster with infra canon (daniel-miller/infra/README.md,
-- mirrored by cmds-app/platform web/src/lib/environments.ts). The 2026-07-31 rename:
-- the pre-production gate takes the name Stage, and Demo becomes the demonstration
-- environment (formerly Preview). Special-purpose environments split into
-- derived-from-live (cold, echo) and independent (demo).
--
-- Also (re)sets is_special_purpose here: migration 070 only updated rows that
-- existed at the time, so a database seeded after 070 ran (the tools/reset-database.ps1
-- flow: migrations against an empty database, then the seed) lost the flags.

ALTER TABLE environment
    ADD COLUMN is_derived_from_live boolean NOT NULL DEFAULT false;

-- Rename the gate first so the demo slug is free for the demonstration environment.
UPDATE environment
   SET environment_slug        = 'stage',
       environment_name        = 'Stage',
       environment_description = 'Pre-production gate with near-parity to production, optional and rarely needed',
       environment_aliases     = ARRAY ['sandbox','stage','staging']::text[],
       updated_at              = now()
 WHERE environment_slug = 'demo';

UPDATE environment
   SET environment_slug        = 'demo',
       environment_name        = 'Demo',
       environment_description = 'Demonstration environment shown to prospects and customers, with curated data rather than production data',
       environment_aliases     = ARRAY ['demo','preview','promo']::text[],
       is_special_purpose      = true,
       updated_at              = now()
 WHERE environment_slug = 'prev';

UPDATE environment
   SET environment_description = 'Continuously refreshed duplicate of live, for operator reporting without loading the live database',
       environment_aliases     = ARRAY ['echo']::text[],
       is_special_purpose      = true,
       is_derived_from_live    = true,
       updated_at              = now()
 WHERE environment_slug = 'echo';

UPDATE environment
   SET environment_description = 'Cold storage of live data for archival and data warehousing',
       environment_aliases     = ARRAY ['cold']::text[],
       is_special_purpose      = true,
       is_derived_from_live    = true,
       updated_at              = now()
 WHERE environment_slug = 'cold';

-- Lifecycle aliases now include the canonical slug itself, matching the canon
-- alias arrays, so slug and alias resolution read from one list.
UPDATE environment
   SET environment_aliases = ARRAY ['local','work']::text[],
       updated_at          = now()
 WHERE environment_slug = 'work';

UPDATE environment
   SET environment_aliases = ARRAY ['dev','development','qa','test','uat']::text[],
       updated_at          = now()
 WHERE environment_slug = 'test';

UPDATE environment
   SET environment_aliases = ARRAY ['live','prod','production']::text[],
       updated_at          = now()
 WHERE environment_slug = 'live';
