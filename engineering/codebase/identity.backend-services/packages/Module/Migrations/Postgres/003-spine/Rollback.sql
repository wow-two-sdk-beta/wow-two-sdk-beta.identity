-- 003-spine rollback — drops the sessions, sign-in log and Data Protection key tables (PostgreSQL). Dev and test
-- recovery only: every session, logged attempt and ring key goes with them, so every cookie dies. No foreign keys, so
-- the order is free.

DROP TABLE IF EXISTS {{schema}}.identity_data_protection_keys;
DROP TABLE IF EXISTS {{schema}}.identity_sign_in_log;
DROP TABLE IF EXISTS {{schema}}.identity_sessions;
