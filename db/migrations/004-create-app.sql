-- 004: App schema

CREATE TABLE IF NOT EXISTS app
(
    app_key         serial       PRIMARY KEY,
    app_slug        varchar(50)  NOT NULL,
    app_name        varchar(200) NOT NULL,
    app_description varchar(500) NULL,

    version_major int          NOT NULL DEFAULT 0,
    version_minor int          NOT NULL DEFAULT 0,
    version_patch int          NOT NULL DEFAULT 1,

    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_app_slug ON app (app_slug);
