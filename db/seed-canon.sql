-- seed-canon.sql: infrastructure canon rosters from daniel-miller/infra/README.md.
-- Applied to every environment (work and live) after migrations, by
-- tools/reset-database.ps1 locally and ops/recreate-database.ps1 on the server.
-- Owner and environment numbers are roster positions: the owner number and
-- environment digit referenced by IIS site IDs. Records of numbers already
-- assigned, never reordered; new entries append at the bottom.

INSERT INTO owner (owner_number, owner_handle, owner_name, owner_description, owner_host)
VALUES (1, 'share',      'Share',                                     'Global owner for data shareable across owners', NULL),
       (2, 'threadwork', 'Threadwork Software',                       'Miller Databases', NULL),
       (3, 'nmb',        'Northern Mat and Bridge',                   NULL, NULL),
       (4, 'pgmf',       'Prince George and District Music Festival', NULL, NULL),
       (5, 'lemar',      'Ground Keepers Friend',                     'Formerly Lemar Tree Spades', NULL),
       (6, 'cmds',       'CMDS',                                      'Keyera''s Competency Management and Development System support team', 'status.cmds.app'),
       (7, 'openscorm',  'OpenSCORM',                                 'Open SCORM, the host organization for Scoop', 'status.openscorm.com'),
       (8, 'djm',        'Daniel Miller',                             'Personal pet projects (initials from Daniel James Miller)', NULL);

INSERT INTO environment (environment_number, environment_handle, environment_name, environment_description, environment_aliases, is_special_purpose, is_derived_from_live)
VALUES
    (1, 'live',  'Live',  'Production environment with real users and real data', ARRAY ['live','prod','production']::text[], false, false),
    (2, 'stage', 'Stage', 'Pre-production gate with near-parity to production, optional and rarely needed', ARRAY ['sandbox','stage','staging']::text[], false, false),
    (3, 'test',  'Test',  'Testing environment for: automated tests, unit tests, integration tests (test); manual quality assurance tests by internal teams (qa); manual user acceptance tests by external teams for customer/user signoff (uat)', ARRAY ['dev','development','qa','test','uat']::text[], false, false),
    (4, 'work',  'Work',  'Local development work environment (internal and private) for rapid iteration and debugging', ARRAY ['local','work']::text[], false, false),

    (5, 'cold',  'Cold',  'Copy of live data that is not kept current: archival, data warehousing, and offline analysis. Cold means stale, not slow - it may be queried freely; it simply is not tracking live', ARRAY ['cold']::text[], true, true),
    (6, 'echo',  'Echo',  'Hot, continuously refreshed duplicate of live, for disaster recovery and for operator reporting without loading the live database', ARRAY ['echo']::text[], true, true),

    (7, 'demo',  'Demo',  'Demonstration environment shown to prospects and customers, and the working environment for sales, marketing, and customer support: prospect demos, customer onboarding, and training on how to use the platform. Curated data chosen to look good rather than to match production', ARRAY ['demo','preview','promo']::text[], true, false);

INSERT INTO server (server_number, server_handle, server_name)
VALUES ( 1, 'a', 'Alpha'),
       ( 2, 'b', 'Bravo'),
       ( 3, 'c', 'Charlie'),
       ( 4, 'd', 'Delta'),
       ( 5, 'e', 'Echo'),
       ( 6, 'f', 'Foxtrot'),
       ( 7, 'g', 'Golf'),
       ( 8, 'h', 'Hotel'),
       ( 9, 'i', 'India'),
       (10, 'j', 'Juliet'),
       (11, 'k', 'Kilo'),
       (12, 'l', 'Lima'),
       (13, 'm', 'Mike'),
       (14, 'n', 'November'),
       (15, 'o', 'Oscar'),
       (16, 'p', 'Papa'),
       (17, 'q', 'Quebec'),
       (18, 'r', 'Romeo'),
       (19, 's', 'Sierra'),
       (20, 't', 'Tango'),
       (21, 'u', 'Uniform'),
       (22, 'v', 'Victor'),
       (23, 'w', 'Whiskey'),
       (24, 'x', 'X-ray'),
       (25, 'y', 'Yankee'),
       (26, 'z', 'Zulu');

-- App tokens follow the naming convention: short single-word codenames.
-- Every app belongs to an owner (070). Deployed apps report problems and bump
-- versions under these handles; an unknown handle is refused rather than
-- registered, so a new app is added here or through POST /api/apps first.
-- app_aliases carries former handles that reporting repos still send, until
-- those repos switch: bridge's build reports as bridgemarket, and openscorm's
-- as slate.
-- Versions are pinned to what live reported as of 2026-10-22, so a fresh
-- database starts at the real numbers instead of 0.0.1.
INSERT INTO app (app_handle, app_name, app_description, owner_key, app_aliases, version_major, version_minor, version_patch)
SELECT v.app_handle, v.app_name, v.app_description, o.owner_key, v.app_aliases, v.major, v.minor, v.patch
  FROM (VALUES
               ('vesper',        'CMDS v5',        'CMDS v5',                                                  'cmds',      ARRAY []::text[],              5, 0, 91),
               ('quad',          'CMDS v4',        NULL,                                                       'cmds',      ARRAY []::text[],              0, 0, 1),
               ('ghost',         'Ghost',          NULL,                                                       'cmds',      ARRAY []::text[],              0, 0, 1),

               ('bump',          'Bump',           'Status, versions, and problem reports (this application)', 'djm',       ARRAY []::text[],              0, 3, 67),
               ('portfolio',     'Portfolio',      NULL,                                                       'djm',       ARRAY []::text[],              0, 0, 1),
               ('pourdecisions', 'Pour Decisions', NULL,                                                       'djm',       ARRAY []::text[],              0, 0, 8),

               ('bridge',        'Bridge',         'BridgeMarket web application for Northern Mat and Bridge', 'nmb',       ARRAY ['bridgemarket']::text[], 6, 1, 21),

               ('scoop',         'Scoop',          'OpenSCORM platform API and web application',               'openscorm', ARRAY ['slate']::text[],        1, 3, 312),

               ('festival',      'Festival',       'Prince George and District Music Festival website',        'pgmf',      ARRAY []::text[],              8, 0, 29))

      AS v(app_handle, app_name, app_description, owner_handle, app_aliases, major, minor, patch)
           JOIN owner o
           ON o.owner_handle = v.owner_handle;

-- Monitoring roster: the live services bump probes. Every service belongs to
-- an app; a site with nothing to report belongs to the app that serves it
-- (ghost for the Ghost marketing sites). A handle is the deployed package's
-- name, lowercased with dots as hyphens (Vesper.Api is vesper-api); a site
-- with no package keeps <site>-www (071).
INSERT INTO service (service_handle, service_name, service_url, owner_key, environment_key, app_key)
SELECT s.service_handle, s.service_name, s.service_url, o.owner_key, e.environment_key, a.app_key
  FROM (VALUES
               ('vesper-api',         'CMDS v5 API',                       'https://live-api.cmds.app/diagnostic/health', 'cmds',      'live', 'vesper'),
               ('vesper-web',         'CMDS v5 web app',                   'https://live.cmds.app',                       'cmds',      'live', 'vesper'),
               ('vesper-saml',        'CMDS v5 SSO',                       'https://hub.cmds.app/diagnostic/health',      'cmds',      'live', 'vesper'),
               ('vesper-verify',      'CMDS v5 certificate verification',  'https://cmds.app/healthz',                    'cmds',      'live', 'vesper'),
               ('shift-ui',           'CMDS v4 web app',                   'https://keyera.cmds.app',                     'cmds',      'live', 'quad'),
               ('keyeracmds-www',     'Keyera CMDS website',               'https://www.keyeracmds.com',                  'cmds',      'live', 'ghost'),
               ('myorientations-www', 'My Orientations website',           'https://www.myorientations.com',              'cmds',      'live', 'ghost'),

               ('danielmiller-www',   'Daniel''s Career Portfolio',        'https://danielmiller.ca',                     'djm',       'live', 'portfolio'),

               ('bridge-web',         'BridgeMarket Web Application (UI)', 'https://prod-nmb.bridgemarket.app',           'nmb',       'live', 'bridge'),

               ('slate-api',          'OpenSCORM API',                     'https://live.openscorm.com/api/health',       'openscorm', 'live', 'scoop'),
               ('slate-web',          'OpenSCORM Application',             'https://app.openscorm.com',                   'openscorm', 'live', 'scoop'),
               ('slate-www',          'OpenSCORM Website',                 'https://www.openscorm.com',                   'openscorm', 'live', 'scoop'),

               ('festival-www',       'PGMF Website and Application',      'https://www.pgmusicfestival.com',             'pgmf',      'live', 'festival'))

      AS s(service_handle, service_name, service_url, owner_handle, environment_handle, app_handle)
           JOIN owner o
           ON o.owner_handle = s.owner_handle
           JOIN environment e
           ON e.environment_handle = s.environment_handle
           JOIN app a
           ON a.app_handle = s.app_handle
ORDER BY o.owner_number, s.service_handle;
