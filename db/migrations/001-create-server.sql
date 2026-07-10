-- 001: Server (host running one or more app deployments).

CREATE TABLE IF NOT EXISTS server
(
    server_key         serial        PRIMARY KEY,
    server_slug        varchar(60)   NOT NULL,
    server_name        varchar(100)  NOT NULL,
    server_description varchar(500),

    created_at         timestamptz   NOT NULL DEFAULT now(),
    updated_at         timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_server_slug ON server (server_slug);
