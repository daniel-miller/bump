-- 003: Environment (one deployment of an owner's system, isolated from that
-- owner's other deployments). Roster follows daniel-miller/infra/README.md.
--
-- environment_number is the position in the infra environment roster: the
-- environment digit in an IIS site ID (1 = live, 2 = stage, 3 = test,
-- 4 = work, 5 = cold, 6 = echo, 7 = demo). Never reordered; new environments
-- append at the bottom.
--
-- environment_aliases holds alternate names accepted from clients, including
-- the canonical handle itself, so handle and alias resolution read from one list
-- (e.g. {"live","prod","production"} for canonical handle "live"). Aliases are
-- for reading, never for writing: the canonical handle is the only token that
-- appears in paths, database names, and config keys.
--
-- is_special_purpose marks environments outside the lifecycle (cold, echo,
-- demo); is_derived_from_live splits those into copies of production data
-- (cold, echo) and independent curated data (demo).

CREATE TABLE IF NOT EXISTS environment
(
    environment_key         serial        PRIMARY KEY,
    environment_number      smallint,
    environment_handle        varchar(60)   NOT NULL,
    environment_name        varchar(100)  NOT NULL,
    environment_description varchar(500),
    environment_aliases     text[]        NOT NULL DEFAULT ARRAY[]::text[],
    is_special_purpose      boolean       NOT NULL DEFAULT false,
    is_derived_from_live    boolean       NOT NULL DEFAULT false,

    created_at              timestamptz   NOT NULL DEFAULT now(),
    updated_at              timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_environment_handle   ON environment (environment_handle);
CREATE UNIQUE INDEX IF NOT EXISTS ix_environment_number ON environment (environment_number);
