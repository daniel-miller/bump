-- 023: Problem schema aligned to RFC 7807
--
-- Column widths are sized at or above the per-field caps enforced in
-- Bump.Api/Limits.cs. Problem details and exception graphs are left as
-- unbounded text/jsonb (PostgreSQL stores them out-of-line via TOAST),
-- but the API still caps their incoming size.
--
-- Problems are scoped directly to (app, environment) rather than to a
-- service row. This lets callers submit problems for apps that aren't
-- under active monitoring (no service registered) — useful for crash
-- reports from one-shot CLIs, batch jobs, or pre-production tooling.

CREATE TABLE IF NOT EXISTS problem
(
    problem_key             bigserial       PRIMARY KEY,
    problem_fingerprint     varchar(16)     NOT NULL,
    problem_exception       jsonb,

    reported_at             timestamptz     NOT NULL DEFAULT now(),
    dispatched_at           timestamptz,
    resolved_at             timestamptz,

    problem_type            varchar(500)    NOT NULL,
    problem_title           varchar(500)    NOT NULL,
    problem_status          int,
    problem_detail          text,
    problem_instance        varchar(2048),
    problem_extensions      jsonb,

    app_key                 int             NOT NULL REFERENCES app(app_key)                 ON DELETE CASCADE,
    environment_key         int             NOT NULL REFERENCES environment(environment_key) ON DELETE CASCADE,

    account_id              uuid,
    account_email           varchar(254)
);

CREATE INDEX IF NOT EXISTS ix_problem_fingerprint ON problem (problem_fingerprint);
CREATE INDEX IF NOT EXISTS ix_problem_reported_at ON problem (reported_at DESC);
CREATE INDEX IF NOT EXISTS ix_problem_app         ON problem (app_key);
CREATE INDEX IF NOT EXISTS ix_problem_environment ON problem (environment_key);
CREATE INDEX IF NOT EXISTS ix_problem_dispatched  ON problem (problem_fingerprint) WHERE dispatched_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_problem_unresolved  ON problem (reported_at DESC) WHERE resolved_at IS NULL;
