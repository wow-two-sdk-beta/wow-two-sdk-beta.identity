-- 001-accounts rollback — drops the 11 identity tables with their indexes (PostgreSQL). Dev and test recovery only.
-- The schema stays: it also holds the set's history and owner marker. No foreign keys, so the order is free.

DROP TABLE IF EXISTS {{schema}}.identity_memberships;
DROP TABLE IF EXISTS {{schema}}.identity_organizations;
DROP TABLE IF EXISTS {{schema}}.identity_passkeys;
DROP TABLE IF EXISTS {{schema}}.identity_refresh_tokens;
DROP TABLE IF EXISTS {{schema}}.identity_user_tokens;
DROP TABLE IF EXISTS {{schema}}.identity_user_logins;
DROP TABLE IF EXISTS {{schema}}.identity_role_claims;
DROP TABLE IF EXISTS {{schema}}.identity_user_claims;
DROP TABLE IF EXISTS {{schema}}.identity_user_roles;
DROP TABLE IF EXISTS {{schema}}.identity_roles;
DROP TABLE IF EXISTS {{schema}}.identity_users;
