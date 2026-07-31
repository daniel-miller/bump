-- 091: Optional per-tenant theme for status boards. JSON object of design
-- tokens (light/dark color maps, radius, font key) plus asset paths (logo,
-- favicon) that the SPA applies at runtime when the board is served from
-- the tenant's custom hostname. NULL means the default Bump look.

ALTER TABLE tenant
    ADD COLUMN IF NOT EXISTS tenant_theme jsonb;
