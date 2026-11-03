-- 010-trusted-contacts rollback — drops the trusted contacts and the columns naming them (PostgreSQL). Dev and test
-- recovery only: every account signing in through a contact loses that way in, the sessions started through one stay
-- live without the mark, and pending invitations forget their contact.

ALTER TABLE {{schema}}.identity_sign_in_log DROP COLUMN IF EXISTS trusted_contact_user_id;
ALTER TABLE {{schema}}.identity_sessions DROP COLUMN IF EXISTS trusted_contact_user_id;
ALTER TABLE {{schema}}.identity_invitations DROP COLUMN IF EXISTS trusted_contact_user_id;
DROP TABLE IF EXISTS {{schema}}.identity_trusted_contacts;
