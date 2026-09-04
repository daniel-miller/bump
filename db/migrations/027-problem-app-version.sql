-- 027: Record the reporting build's version on each problem
--
-- A problem row had no version of its own. Readers joined app.version_*,
-- which is the registry number the deploy-time bump step (or a build script
-- calling POST /api/apps/{handle}/version/bumps) last set. That number moves
-- when a build is cut, not when it reaches an environment, so a fault on a
-- live 1.3.159 process was rendered as v1.3.160 while 1.3.160 existed only
-- on test. The reporter knows which build it is; store what it says.
--
-- Nullable on purpose: a consumer whose client sends no Version keeps
-- working, and readers fall back to the registry number for those rows.

ALTER TABLE problem ADD COLUMN IF NOT EXISTS app_version varchar(100);

COMMENT ON COLUMN problem.app_version IS
    'Version the reporting process declared for itself, e.g. 1.3.174+db890ee. NULL when the reporter sent none; readers then show the app registry version, which is the last build cut rather than the build that threw.';
