-- 012-device-confirmation-request — what a sign-in waiting for a contact's press keeps of the request that made it
-- (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- An expand step on an unpublished table. is_cookie_client says how the client that asked proves itself when it polls: a
-- browser by its device cookie, a bearer client by the token alone. A request waiting while this applies counts as a
-- browser's, which at worst sends a bearer client back to start again. ip_masked and user_agent go into the sign-in log when a
-- contact denies the request, since that press reaches the instance through Telegram, not from the browser that asked.

ALTER TABLE {{schema}}.identity_device_confirmations ADD COLUMN IF NOT EXISTS is_cookie_client boolean NOT NULL DEFAULT true;
ALTER TABLE {{schema}}.identity_device_confirmations ADD COLUMN IF NOT EXISTS ip_masked varchar(64) NULL;
ALTER TABLE {{schema}}.identity_device_confirmations ADD COLUMN IF NOT EXISTS user_agent varchar(256) NULL;
