-- 071: Service handles follow the deployed package name.
--
-- A service handle is the name of the package it deploys, lowercased, with
-- dots as hyphens: Vesper.Api is vesper-api. The live service takes the bare
-- name and a service in any other environment adds the environment as a
-- suffix (vesper-api-test), because handles are unique across all of Bump,
-- not per environment. A site with no package of its own keeps <site>-www.
--
-- The CMDS handles below still carried the retired Prism and Spark names, so
-- the handle said nothing about what was deployed: prism-api probes the
-- Vesper.Api package, spark-hub the Vesper.Web site, and prism-web the
-- Shift UI site (InSite.UI) that serves CMDS v4.
--
-- Probe history, daily uptime, state, and outages all reference service_key,
-- so a rename carries every record with it. Keyed by handle, so this does
-- nothing on a fresh database, where seed-canon.sql writes the new handles.

UPDATE service s
   SET service_handle = v.new_handle,
       updated_at     = now()
  FROM (VALUES ('prism-api', 'vesper-api'),
               ('spark-hub', 'vesper-web'),
               ('prism-web', 'shift-ui'))
       AS v(old_handle, new_handle)
 WHERE s.service_handle = v.old_handle
   AND NOT EXISTS (SELECT 1 FROM service WHERE service_handle = v.new_handle);
