-- 002: Owner (the party whose deployment this is: a customer, an internal
-- business unit, or a product line). This tier matches the owner tier in the
-- runtime path grammar srv/host/owner/environment/collection defined in
-- daniel-miller/infra/README.md.
--
-- owner_number is the position in the infra owner roster: the two-digit
-- owner number (01-99) referenced by IIS site IDs. The roster is a record of
-- numbers already assigned, never reordered. NULL for ad hoc owners created
-- in the admin UI that are not part of the infrastructure roster.
--
-- owner_host: when a request arrives with a Host header matching owner_host,
-- the SPA serves that owner's status board at the site root
-- (e.g. status.openscorm.com -> /boards/openscorm).
--
-- owner_theme: JSON object of design tokens (light/dark color maps, radius,
-- font key) plus asset paths (logo, favicon) that the SPA applies at runtime
-- when the board is served from the owner's custom hostname. NULL means the
-- default Bump look.

CREATE TABLE IF NOT EXISTS owner
(
    owner_key         serial        PRIMARY KEY,
    owner_number      smallint,
    owner_handle        varchar(60)   NOT NULL,
    owner_name        varchar(100)  NOT NULL,
    owner_description varchar(500),
    owner_host        varchar(255),
    owner_theme       jsonb,

    created_at        timestamptz   NOT NULL DEFAULT now(),
    updated_at        timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_owner_handle   ON owner (owner_handle);
CREATE UNIQUE INDEX IF NOT EXISTS ix_owner_number ON owner (owner_number);
CREATE UNIQUE INDEX IF NOT EXISTS ix_owner_host   ON owner (lower(owner_host));
