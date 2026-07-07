-- 002: Tenant (logical owner of monitored resources).

CREATE TABLE IF NOT EXISTS tenant
(
    tenant_key         serial        PRIMARY KEY,
    tenant_slug        varchar(60)   NOT NULL,
    tenant_name        varchar(100)  NOT NULL,
    tenant_description varchar(500),

    created_at         timestamptz   NOT NULL DEFAULT now(),
    updated_at         timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_slug ON tenant (tenant_slug);
