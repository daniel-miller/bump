-- 070: Mark special-purpose environments (echo, cold, prev) so the UI can
-- visually distinguish them from regular deployment stages.

ALTER TABLE environment
    ADD COLUMN IF NOT EXISTS is_special_purpose boolean NOT NULL DEFAULT false;

UPDATE environment
   SET is_special_purpose = true
 WHERE environment_slug IN ('echo', 'cold', 'prev');
