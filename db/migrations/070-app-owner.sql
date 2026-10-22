-- 070: Every app belongs to an owner, and every service belongs to an app.
--
-- 020 left service.app_key nullable because not every probed site had an
-- application reporting to Bump. In practice the gap only grew: the admin
-- create-service form had no app field, so every service added through it
-- arrived unlinked, with no route to its problem reports. Sites that report
-- nothing now belong to the app that serves them (ghost for the Ghost
-- marketing sites), so the link can be required. Deleting an app that still
-- has services is refused rather than detaching them.
--
-- owner_key places each app on one owner's roster, the tier services already
-- carry. Deleting an owner that still has apps is refused for the same reason.
--
-- app_aliases holds former handles still accepted from reporters, so a rename
-- does not have to land in the same hour as every repo that reports under the
-- old handle. Unlike environment_aliases it does not repeat the canonical
-- handle: each entry is a handle the app used to have. Aliases are for
-- reading (problem reports, version reads and bumps), never for writing.
--
-- The data steps bring the live roster into line before the constraints land.
-- They are keyed by handle, so on a fresh database (empty app and service
-- tables at this point) they do nothing and seed-canon.sql supplies the same
-- canon afterwards. A live row left unassigned makes SET NOT NULL fail, the
-- migration roll back, and the API refuse to start: loud on purpose.

ALTER TABLE app ADD COLUMN IF NOT EXISTS owner_key   int    REFERENCES owner(owner_key) ON DELETE RESTRICT;
ALTER TABLE app ADD COLUMN IF NOT EXISTS app_aliases text[] NOT NULL DEFAULT ARRAY[]::text[];

CREATE INDEX IF NOT EXISTS ix_app_owner ON app (owner_key);

-- bridgemarket was a second registration of Bridge, made when its build
-- started reporting under that handle. Fold it into bridge: it carries the
-- version Bridge's builds have been bumping, and the handle stays usable as
-- an alias until the bridge repo switches.
UPDATE app b
   SET version_major = m.version_major,
       version_minor = m.version_minor,
       version_patch = m.version_patch,
       updated_at    = now()
  FROM app m
 WHERE b.app_handle = 'bridge'
   AND m.app_handle = 'bridgemarket'
   AND (m.version_major, m.version_minor, m.version_patch)
     > (b.version_major, b.version_minor, b.version_patch);

UPDATE problem
   SET app_key = (SELECT app_key FROM app WHERE app_handle = 'bridge')
 WHERE app_key = (SELECT app_key FROM app WHERE app_handle = 'bridgemarket')
   AND EXISTS (SELECT 1 FROM app WHERE app_handle = 'bridge');

UPDATE service
   SET app_key = (SELECT app_key FROM app WHERE app_handle = 'bridge')
 WHERE app_key = (SELECT app_key FROM app WHERE app_handle = 'bridgemarket')
   AND EXISTS (SELECT 1 FROM app WHERE app_handle = 'bridge');

DELETE FROM app
 WHERE app_handle = 'bridgemarket'
   AND EXISTS (SELECT 1 FROM app WHERE app_handle = 'bridge');

UPDATE app
   SET app_aliases = array_append(app_aliases, 'bridgemarket')
 WHERE app_handle = 'bridge'
   AND NOT 'bridgemarket' = ANY (app_aliases);

-- Slate returns to its codename. The openscorm repo reports as slate until it
-- switches, so the old handle stays as an alias.
UPDATE app
   SET app_handle  = 'scoop',
       app_name    = 'Scoop',
       app_aliases = array_append(app_aliases, 'slate'),
       updated_at  = now()
 WHERE app_handle = 'slate'
   AND NOT EXISTS (SELECT 1 FROM app WHERE app_handle = 'scoop');

UPDATE app
   SET app_name        = 'CMDS v5',
       app_description = 'CMDS v5',
       updated_at      = now()
 WHERE app_handle = 'vesper';

INSERT INTO app (app_handle, app_name, owner_key)
SELECT v.app_handle, v.app_name, o.owner_key
  FROM (VALUES ('quad',         'CMDS v4',       'cmds'),
               ('ghost',        'Ghost',         'cmds'),
               ('portfolio',    'Portfolio',     'djm'))
       AS v(app_handle, app_name, owner_handle)
  JOIN owner o ON o.owner_handle = v.owner_handle
    ON CONFLICT (app_handle) DO NOTHING;

UPDATE app a
   SET owner_key = o.owner_key
  FROM (VALUES ('bump',          'djm'),
               ('vesper',        'cmds'),
               ('quad',          'cmds'),
               ('ghost',         'cmds'),
               ('bridge',        'nmb'),
               ('scoop',         'openscorm'),
               ('festival',      'pgmf'),
               ('portfolio',     'djm'),
               ('pourdecisions', 'djm'))
       AS v(app_handle, owner_handle)
  JOIN owner o ON o.owner_handle = v.owner_handle
 WHERE a.app_handle = v.app_handle
   AND a.owner_key IS NULL;

UPDATE service s
   SET app_key = a.app_key
  FROM (VALUES ('prism-api',          'vesper'),
               ('vesper-saml',        'vesper'),
               ('vesper-verify',      'vesper'),
               ('prism-web',          'quad'),
               ('keyeracmds-www',     'ghost'),
               ('myorientations-www', 'ghost'),
               ('danielmiller-www',   'portfolio'))
       AS v(service_handle, app_handle)
  JOIN app a ON a.app_handle = v.app_handle
 WHERE s.service_handle = v.service_handle
   AND s.app_key IS NULL;

ALTER TABLE app     ALTER COLUMN owner_key SET NOT NULL;
ALTER TABLE service ALTER COLUMN app_key   SET NOT NULL;

ALTER TABLE service DROP CONSTRAINT IF EXISTS service_app_key_fkey;
ALTER TABLE service ADD  CONSTRAINT service_app_key_fkey
    FOREIGN KEY (app_key) REFERENCES app(app_key) ON DELETE RESTRICT;
