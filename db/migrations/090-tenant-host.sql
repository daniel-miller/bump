-- 090: Optional custom hostname per tenant. When a request arrives with a
-- Host header matching tenant_host, the SPA serves that tenant's status
-- board at the site root (e.g. status.openscorm.com -> /tenants/openscorm).

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS tenant_host varchar(255);

CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_host ON tenant (lower(tenant_host));
