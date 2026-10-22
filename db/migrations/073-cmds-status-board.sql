-- 073: The CMDS board is served at status.cmds.app, in the CMDS theme.
--
-- Same pattern as status.openscorm.com: a request whose Host header matches
-- owner_host gets that owner's board at the site root, restyled by
-- owner_theme. The hostname also needs a DNS record and an IIS binding on
-- the Bump site; neither lives in this database.
--
-- The host is only set where the cmds owner has none, so a hostname chosen
-- later in the admin UI is not overwritten. The theme is the one in
-- db/seed-cmds-theme.sql, which carries the reasoning; change both together.
-- Keyed by owner handle, so this does nothing on a fresh database, where
-- seed-canon.sql and the theme seed supply the same values.

UPDATE owner
   SET owner_host = 'status.cmds.app',
       updated_at = now()
 WHERE owner_handle = 'cmds'
   AND owner_host IS NULL
   AND NOT EXISTS (SELECT 1 FROM owner WHERE lower(owner_host) = 'status.cmds.app');

UPDATE owner
   SET owner_theme = '{
         "font": "inter",
         "favicon": "/themes/cmds/favicon-32.png",
         "radius": { "card": "0.75rem", "badge": "0.375rem" },
         "light": {
           "background": "#f7f5f0",
           "foreground": "#293342",
           "foreground-strong": "#0f182b",
           "link": "#3b3fd3",
           "muted": "#f3f5f8",
           "muted-foreground": "#677283",
           "border": "#e1e5ea",
           "card": "#ffffff",
           "card-foreground": "#293342",
           "primary": "#3b3fd3",
           "primary-foreground": "#fafafa",
           "success": "#24c369",
           "warning": "#f2ab19",
           "danger": "#f13936"
         },
         "dark": {
           "background": "#0b0f18",
           "foreground": "#dadee5",
           "foreground-strong": "#f3f5f8",
           "link": "#7e9eff",
           "muted": "#1b1f29",
           "muted-foreground": "#9199a5",
           "border": "#242933",
           "card": "#12161f",
           "card-foreground": "#dadee5",
           "primary": "#6275fc",
           "primary-foreground": "#fafafa",
           "success": "#43cc76",
           "warning": "#feba08",
           "danger": "#ff5149"
         }
       }'::jsonb,
       updated_at = now()
 WHERE owner_handle = 'cmds'
   AND owner_theme IS NULL;
