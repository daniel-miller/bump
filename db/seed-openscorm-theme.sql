-- Seed the OpenSCORM owner theme for status.openscorm.com.
-- Token values come from the OpenSCORM design system (tokens/colors.css):
-- green #16a34a is the only accent (CTA fill deepens to #15803d for AA),
-- mint #6ee7a3 replaces it for text accents on the dark navy surface,
-- and the dark page sits on navy #030620 with slate-900 cards.
-- Warning amber is Bump's functional status color, kept deliberately:
-- the design system bans orange as a brand accent, but a status board
-- needs a middle severity between operational green and outage red, and
-- every status cue here is also carried by a text label.

UPDATE owner
   SET owner_theme = '{
         "font": "inter",
         "logo": "/themes/openscorm/logo-mark.svg",
         "favicon": "/themes/openscorm/favicon-32.png",
         "radius": { "card": "12px", "badge": "9999px" },
         "light": {
           "background": "#ffffff",
           "foreground": "#101010",
           "muted": "#f3f4f6",
           "muted-foreground": "rgb(16 16 16 / 66%)",
           "border": "#e5e7eb",
           "card": "#ffffff",
           "card-foreground": "#101010",
           "primary": "#15803d",
           "primary-foreground": "#ffffff",
           "success": "#16a34a",
           "warning": "#f5a623",
           "danger": "#ef4444"
         },
         "dark": {
           "background": "#030620",
           "foreground": "#e5ecf6",
           "muted": "#1e293b",
           "muted-foreground": "rgb(229 236 246 / 66%)",
           "border": "#334155",
           "card": "#0f172a",
           "card-foreground": "#e5ecf6",
           "primary": "#6ee7a3",
           "primary-foreground": "#030620",
           "success": "#16a34a",
           "warning": "#f5a623",
           "danger": "#ef4444"
         }
       }'::jsonb,
       updated_at = now()
 WHERE lower(owner_host) = 'status.openscorm.com';
