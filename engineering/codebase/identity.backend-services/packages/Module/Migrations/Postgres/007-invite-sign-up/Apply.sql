-- 007-invite-sign-up — signing up from an invitation with a name, a picture and a phone the bot verifies (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only, on tables not published to products. A start link opened for an invitation sign-up learns its
-- number from the shared contact, so phone_number becomes optional and reference_id names the sign-up it completes.

ALTER TABLE {{schema}}.identity_telegram_link_requests ALTER COLUMN phone_number DROP NOT NULL;
ALTER TABLE {{schema}}.identity_telegram_link_requests ADD COLUMN IF NOT EXISTS reference_id uuid NULL;

-- One row per sign-up a browser started from an invitation: the name and processed picture it gave, waiting for the bot
-- to prove a phone. Only the browser holding the secret polls it; it signs that browser in once. Rows a day past their
-- expiry are pruned as new ones arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_sign_up_requests (
    id                  uuid         NOT NULL,
    invitation_id       uuid         NOT NULL,
    display_name        varchar(120) NULL,
    picture             bytea        NULL,
    browser_secret_hash varchar(64)  NOT NULL,
    client_id           varchar(100) NULL,
    created_at          timestamptz  NOT NULL,
    expires_at          timestamptz  NOT NULL,
    completed_at        timestamptz  NULL,
    user_id             uuid         NULL,
    failure_code        varchar(64)  NULL,
    signed_in_at        timestamptz  NULL,
    CONSTRAINT pk_identity_sign_up_requests PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_identity_sign_up_requests_expires_at ON {{schema}}.identity_sign_up_requests (expires_at);

-- One profile picture per person: the processed square WebP, behind IProfilePictureStore. Erasure deletes the row.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_profile_pictures (
    user_id      uuid        NOT NULL,
    content      bytea       NOT NULL,
    content_type varchar(64) NOT NULL,
    version      varchar(64) NOT NULL,
    updated_at   timestamptz NOT NULL,
    CONSTRAINT pk_identity_profile_pictures PRIMARY KEY (user_id)
);
