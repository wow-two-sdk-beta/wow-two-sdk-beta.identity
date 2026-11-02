-- 006-invitations — invitations into an organization, each a single-use link carrying a role (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Rules as in 001: no foreign keys, instants are timestamptz, lengths are the model's. Not published to products. The
-- link's token never lands in clear: only its SHA-256 is stored.

-- One row per invitation an owner or admin made. It redeems while accepted_at and revoked_at are null and expires_at lies
-- ahead; accepted_at is set by a conditional update, so two redeemers at once spend it once. A row naming phone_number or
-- email redeems only for that identifier.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_invitations (
    id                  uuid         NOT NULL,
    organization_id     uuid         NOT NULL,
    role                varchar(64)  NOT NULL,
    token_hash          varchar(64)  NOT NULL,
    phone_number        varchar(32)  NULL,
    email               varchar(256) NULL,
    created_by_user_id  uuid         NOT NULL,
    created_at          timestamptz  NOT NULL,
    expires_at          timestamptz  NOT NULL,
    accepted_at         timestamptz  NULL,
    accepted_by_user_id uuid         NULL,
    revoked_at          timestamptz  NULL,
    CONSTRAINT pk_identity_invitations PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_invitations_token_hash ON {{schema}}.identity_invitations (token_hash);
CREATE INDEX IF NOT EXISTS ix_identity_invitations_organization_id ON {{schema}}.identity_invitations (organization_id);
