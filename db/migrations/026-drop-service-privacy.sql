-- 026: Drop service.is_private. The flag only hid a service from the admin
-- services list behind a "Show private" toggle; it never affected probing or
-- public boards, and the roster no longer needs private entries.

ALTER TABLE service DROP COLUMN IF EXISTS is_private;
