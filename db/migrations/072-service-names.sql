-- 072: CMDS service names say which version they belong to.
--
-- Names are what an admin and the public cmds board read, and the old ones
-- were ambiguous next to each other. "CMDS API" could be either version's API
-- (api.cmds.app is the v4 Shift API; this one is v5's). "CMDS Hub" used a
-- retired name, and the v5 SSO broker is the service that lives at
-- hub.cmds.app. "CMDS UI" beside "CMDS Hub" did not say which was the old
-- system. Each name now carries v4 or v5, matching the CMDS v4 and CMDS v5
-- apps, and every name is in sentence case.
--
-- Keyed by handle (after 071), so this does nothing on a fresh database,
-- where seed-canon.sql writes the same names.

UPDATE service s
   SET service_name = v.service_name,
       updated_at   = now()
  FROM (VALUES ('vesper-api',         'CMDS v5 API'),
               ('vesper-web',         'CMDS v5 web app'),
               ('vesper-saml',        'CMDS v5 SSO'),
               ('vesper-verify',      'CMDS v5 certificate verification'),
               ('shift-ui',           'CMDS v4 web app'),
               ('keyeracmds-www',     'Keyera CMDS website'),
               ('myorientations-www', 'My Orientations website'))
       AS v(service_handle, service_name)
 WHERE s.service_handle = v.service_handle
   AND s.service_name IS DISTINCT FROM v.service_name;
