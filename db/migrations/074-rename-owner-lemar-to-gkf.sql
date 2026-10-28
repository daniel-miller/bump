-- 074: Owner 5's handle changes from lemar to gkf.
--
-- Lemar Tree Spades is now Ground Keepers Friend and still an active
-- customer, so it keeps roster position 5 in daniel-miller/infra/README.md;
-- only the token changes. The name and description already say Ground
-- Keepers Friend. Other tables point at the owner by owner_key, so nothing
-- else needs to follow the rename.
--
-- Keyed by owner handle, so this does nothing on a fresh database, where
-- seed-canon.sql already supplies gkf.

UPDATE owner
   SET owner_handle = 'gkf',
       updated_at = now()
 WHERE owner_handle = 'lemar'
   AND NOT EXISTS (SELECT 1 FROM owner WHERE owner_handle = 'gkf');
