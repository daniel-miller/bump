-- 080: Mark services as private so the admin services list can hide them
-- by default. Has no effect on probing or public boards.

ALTER TABLE service
    ADD COLUMN IF NOT EXISTS is_private boolean NOT NULL DEFAULT false;
