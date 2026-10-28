-- @no-transaction
--
-- 004-user-soft-delete — soft delete for accounts: identity_users.deleted_at, and the user-name and email indexes
-- scoped to live rows (PostgreSQL).
--
-- Deleting an account sets deleted_at and keeps the row; erasing one also scrubs its personal columns in place. The
-- row never leaves, so a product's RESTRICT key into identity_users never blocks. IdentityDbContext maps the column,
-- hides deleted rows from every query and filters both indexes as here; the Migrations test tier compares the two.
-- {{schema}} is Identity:Database:Schema (default identity).
--
-- An expand: a nullable column without a default, and indexes rebuilt CONCURRENTLY, so the file runs outside a
-- transaction, statement by statement. Every index is dropped right before it is built, so a rerun after an
-- interrupted build replaces an invalid one. The unique user-name index keeps a swap twin while it is rebuilt, so user
-- names stay unique throughout. The email index stays non-unique: UserOptions.RequireUniqueEmail may be off.

ALTER TABLE {{schema}}.identity_users ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL;

-- ── User name: unique among live accounts ───────────────────────────────────────────────────────────────────────────

DROP INDEX CONCURRENTLY IF EXISTS {{schema}}.ix_identity_users_normalized_user_name_swap;
CREATE UNIQUE INDEX CONCURRENTLY ix_identity_users_normalized_user_name_swap
    ON {{schema}}.identity_users (normalized_user_name) WHERE deleted_at IS NULL;

DROP INDEX CONCURRENTLY IF EXISTS {{schema}}.ix_identity_users_normalized_user_name;
CREATE UNIQUE INDEX CONCURRENTLY ix_identity_users_normalized_user_name
    ON {{schema}}.identity_users (normalized_user_name) WHERE deleted_at IS NULL;

DROP INDEX CONCURRENTLY IF EXISTS {{schema}}.ix_identity_users_normalized_user_name_swap;

-- ── Email: lookups over live accounts ───────────────────────────────────────────────────────────────────────────────

DROP INDEX CONCURRENTLY IF EXISTS {{schema}}.ix_identity_users_normalized_email;
CREATE INDEX CONCURRENTLY ix_identity_users_normalized_email
    ON {{schema}}.identity_users (normalized_email) WHERE deleted_at IS NULL;
