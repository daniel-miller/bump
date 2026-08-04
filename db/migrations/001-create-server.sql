-- 001: Server (host running one or more app deployments).
--
-- server_number is the position in the NATO-phonetic roster from
-- daniel-miller/infra/README.md ("Server names"): alpha = 1 ... zulu = 26.
-- It is the S digit in an IIS site ID.

CREATE TABLE IF NOT EXISTS server
(
    server_key         serial        PRIMARY KEY,
    server_number      smallint,
    server_handle      varchar(60)   NOT NULL,
    server_name        varchar(100)  NOT NULL,
    server_description varchar(500),

    created_at         timestamptz   NOT NULL DEFAULT now(),
    updated_at         timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_server_handle   ON server (server_handle);
CREATE UNIQUE INDEX IF NOT EXISTS ix_server_number ON server (server_number);
