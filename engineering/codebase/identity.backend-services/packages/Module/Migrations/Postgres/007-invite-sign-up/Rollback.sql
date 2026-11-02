-- 007-invite-sign-up rollback — drops the sign-up requests, the profile pictures and the start links' reference
-- (PostgreSQL). Dev and test recovery only: every kept picture goes, and start links holding no number are deleted.

DROP TABLE IF EXISTS {{schema}}.identity_profile_pictures;
DROP TABLE IF EXISTS {{schema}}.identity_sign_up_requests;
DELETE FROM {{schema}}.identity_telegram_link_requests WHERE phone_number IS NULL;
ALTER TABLE {{schema}}.identity_telegram_link_requests ALTER COLUMN phone_number SET NOT NULL;
ALTER TABLE {{schema}}.identity_telegram_link_requests DROP COLUMN IF EXISTS reference_id;
