-- 010-trusted-contacts — trusted contacts: another person of the instance whose confirmed, bot-linked phone receives an
-- account's sign-in codes (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only: one new table and three nullable columns with no default, so existing rows stay as they are and no
-- table is rewritten. Rules as in 001: no foreign keys, instants are timestamptz. Nothing here is published to products.

-- One row per account and contact: the contact's Telegram receives the account's codes, with the same power as the
-- account's own phone. An invitation records one when its sign-up goes through the contact; at most
-- Identity:TrustedContacts:MaxPerPerson per account. Revoking one deletes the row.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_trusted_contacts (
    user_id            uuid        NOT NULL,
    contact_user_id    uuid        NOT NULL,
    created_by_user_id uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    CONSTRAINT pk_identity_trusted_contacts PRIMARY KEY (user_id, contact_user_id)
);

CREATE INDEX IF NOT EXISTS ix_identity_trusted_contacts_contact_user_id
    ON {{schema}}.identity_trusted_contacts (contact_user_id);

-- The contact an invitation's maker named: the inviter or another member with a confirmed, bot-linked phone. A sign-up
-- through it sends the code to the contact's Telegram and records the contact for the new person.
ALTER TABLE {{schema}}.identity_invitations ADD COLUMN IF NOT EXISTS trusted_contact_user_id uuid NULL;

-- The contact a session's sign-in went through; null for a sign-in by the account's own means. Revoking the contact ends
-- every session naming it.
ALTER TABLE {{schema}}.identity_sessions ADD COLUMN IF NOT EXISTS trusted_contact_user_id uuid NULL;

-- The contact a logged attempt went through (method trusted_contact_code); null otherwise.
ALTER TABLE {{schema}}.identity_sign_in_log ADD COLUMN IF NOT EXISTS trusted_contact_user_id uuid NULL;
