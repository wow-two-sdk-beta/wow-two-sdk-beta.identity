-- 012-device-confirmation-request rollback — drops what a waiting sign-in keeps of its request (PostgreSQL). Dev and test
-- recovery only: requests waiting at that moment stop binding to their client until it applies again.

ALTER TABLE {{schema}}.identity_device_confirmations DROP COLUMN IF EXISTS user_agent;
ALTER TABLE {{schema}}.identity_device_confirmations DROP COLUMN IF EXISTS ip_masked;
ALTER TABLE {{schema}}.identity_device_confirmations DROP COLUMN IF EXISTS is_cookie_client;
