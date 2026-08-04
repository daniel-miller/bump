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
       (6, 'cmds',       'CMDS',                                      'Keyera''s Competency Management and Development System support team', NULL),
       (7, 'openscorm',  'OpenSCORM',                                 'Open SCORM, the host organization for Scoop', 'status.openscorm.com'),
       (8, 'djm',        'Daniel Miller',                             'Personal pet projects (initials from Daniel James Miller)', NULL);

INSERT INTO environment (environment_number, environment_handle, environment_name, environment_description, environment_aliases, is_special_purpose, is_derived_from_live)
VALUES
    (1, 'live',  'Live',  'Production environment with real users and real data', ARRAY ['live','prod','production']::text[], false, false),
    (2, 'stage', 'Stage', 'Pre-production gate with near-parity to production, optional and rarely needed', ARRAY ['sandbox','stage','staging']::text[], false, false),
    (3, 'test',  'Test',  'Testing environment for: automated tests, unit tests, integration tests (test); manual quality assurance tests by internal teams (qa); manual user acceptance tests by external teams for customer/user signoff (uat)', ARRAY ['dev','development','qa','test','uat']::text[], false, false),
    (4, 'work',  'Work',  'Local development work environment (internal and private) for rapid iteration and debugging', ARRAY ['local','work']::text[], false, false),

    (5, 'cold',  'Cold',  'Cold storage of live data for archival and data warehousing', ARRAY ['cold']::text[], true, true),
    (6, 'echo',  'Echo',  'Continuously refreshed duplicate of live, for operator reporting without loading the live database', ARRAY ['echo']::text[], true, true),

    (7, 'demo',  'Demo',  'Demonstration environment shown to prospects and customers, with curated data rather than production data', ARRAY ['demo','preview','promo']::text[], true, false);

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
-- Deployed apps must report versions under these handles; an unknown handle
-- self-registers a new row, so renaming here requires the reporting app's
-- CI to switch handles at the same time.
-- Versions are pinned to what live reported as of 2026-08-04, so a fresh
-- database starts at the real numbers instead of 0.0.1. On cutover,
-- ops/recreate-database.ps1 captures the versions from the old database
-- after this seed runs, so live's numbers still win if they have moved on.
INSERT INTO app (app_handle, app_name, app_description, version_major, version_minor, version_patch)
VALUES ('bump',     'Bump',     'Status, versions, and problem reports (this application)', 0, 3, 25),
       ('spark',    'Spark',    'CMDS integration hub, formerly Cmds.Hub',                  0, 0, 107),
       ('bridge',   'Bridge',   'BridgeMarket web application for Northern Mat and Bridge', 6, 1, 14),
       ('slate',    'Slate',    'OpenSCORM platform API and web application',               1, 3, 111),
       ('festival', 'Festival', 'Prince George and District Music Festival website',        7, 0, 14);

-- Monitoring roster: the live services bump probes. app is NULL where the
-- probed site has no application reporting versions to bump.
INSERT INTO service (service_handle, service_name, service_url, owner_key, environment_key, app_key, is_private)
SELECT s.service_handle, s.service_name, s.service_url, o.owner_key, e.environment_key, a.app_key, s.is_private
  FROM (VALUES
               ('cmds-api',            'CMDS API',                          'https://live-api.cmds.app',             'cmds',      'live', NULL,       false),
               ('cmds-hub',            'CMDS Hub',                          'https://hub.cmds.app/api/health',       'cmds',      'live', 'spark',    false),
               ('cmds-web',            'CMDS Application',                  'https://keyera.cmds.app',               'cmds',      'live', NULL,       false),
               ('cmds-www',            'CMDS Website',                      'https://www.keyeracmds.com',            'cmds',      'live', NULL,       false),
               ('myorientations-www',  'My Orientations Website',           'https://www.myorientations.com',        'cmds',      'live', NULL,       false),

               ('danielmiller-www',    'Daniel''s Career Portfolio',        'https://danielmiller.ca',               'djm',       'live', NULL,       false),

               ('bridgemarket-web',    'BridgeMarket Web Application (UI)', 'https://prod-nmb.bridgemarket.app',     'nmb',       'live', 'bridge',   true),

               ('openscorm-api',       'OpenSCORM API',                     'https://live.openscorm.com/api/health', 'openscorm', 'live', 'slate',    false),
               ('openscorm-web',       'OpenSCORM Application',             'https://live.openscorm.com',            'openscorm', 'live', 'slate',    false),
               ('openscorm-www',       'OpenSCORM Website',                 'https://www.openscorm.com',             'openscorm', 'live', 'slate',    false),

               ('pgmusicfestival-www', 'PGMF Website and Application',      'https://www.pgmusicfestival.com',       'pgmf',      'live', 'festival', false))

      AS s(service_handle, service_name, service_url, owner_handle, environment_handle, app_handle, is_private)
           JOIN owner o
           ON o.owner_handle = s.owner_handle
           JOIN environment e
           ON e.environment_handle = s.environment_handle
           LEFT JOIN app a
           ON a.app_handle = s.app_handle
ORDER BY o.owner_number, s.service_handle;
