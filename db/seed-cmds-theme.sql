-- Seed the CMDS owner theme for status.cmds.app.
-- Token values come from the CMDS design system (cmds-app/platform
-- design/tokens/colors.css, which matches web/src/styles/globals.css):
-- a warm paper page with white cards and a cool slate ink ramp, indigo as
-- the one accent, and a dark navy surface in dark mode. The tokens are
-- authored in oklch, which the theme validator does not accept (hex and rgb
-- only, so a bad row cannot inject CSS), so each value here is its sRGB
-- conversion. Dark link and danger sit outside sRGB and are gamut-mapped by
-- chroma reduction, the way a browser renders them. Status colors are the
-- CMDS app's own, warning amber included.
--
-- CMDS has no logo mark (the app shows the word CMDS), so the board heading
-- is the owner name alone. The favicon is the CMDS app's 32px icon.
--
-- 073-cmds-status-board.sql applies the same theme to an existing database;
-- change both together.

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
 WHERE lower(owner_host) = 'status.cmds.app';
