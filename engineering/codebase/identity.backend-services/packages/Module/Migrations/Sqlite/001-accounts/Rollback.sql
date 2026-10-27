-- 001-accounts rollback — drops the 11 identity tables with their indexes (SQLite). Dev and test recovery only.
-- No foreign keys, so the order is free.

DROP TABLE IF EXISTS identity_memberships;
DROP TABLE IF EXISTS identity_organizations;
DROP TABLE IF EXISTS identity_passkeys;
DROP TABLE IF EXISTS identity_refresh_tokens;
DROP TABLE IF EXISTS identity_user_tokens;
DROP TABLE IF EXISTS identity_user_logins;
DROP TABLE IF EXISTS identity_role_claims;
DROP TABLE IF EXISTS identity_user_claims;
DROP TABLE IF EXISTS identity_user_roles;
DROP TABLE IF EXISTS identity_roles;
DROP TABLE IF EXISTS identity_users;
