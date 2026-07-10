-- 003: Environment (deployment stage: prod, staging, etc).
--
-- environment_aliases holds alternate names accepted from clients
-- (e.g. {"production","prd"} matching the canonical environment_slug "prod").

CREATE TABLE IF NOT EXISTS environment
(
    environment_key         serial        PRIMARY KEY,
    environment_slug        varchar(60)   NOT NULL,
    environment_name        varchar(100)  NOT NULL,
    environment_description varchar(500),
    environment_aliases     text[]        NOT NULL DEFAULT ARRAY[]::text[],
    is_special_purpose      boolean       NOT NULL DEFAULT false,

    created_at              timestamptz   NOT NULL DEFAULT now(),
    updated_at              timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_environment_slug ON environment (environment_slug);
