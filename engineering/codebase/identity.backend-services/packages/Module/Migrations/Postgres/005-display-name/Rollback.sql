-- 005-display-name rollback — drops the display name column (PostgreSQL). Dev and test recovery only: every person's
-- display name goes with it, and the name claim falls back to the user name, email or phone.

ALTER TABLE {{schema}}.identity_users DROP COLUMN IF EXISTS display_name;
