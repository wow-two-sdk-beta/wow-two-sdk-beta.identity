-- 001-accounts — the identity schema's first 11 tables: accounts, roles, claims, logins, stored tokens, refresh tokens,
-- passkeys, organizations and memberships (SQLite).
--
-- The canonical schema: IdentityDbContext maps over it column for column and never generates it; the Migrations test
-- tier compares the two. SQLite has no schemas: the identity_ prefix keeps these tables apart from a product's.
--
-- Storage, as the context maps it: uuid ids as TEXT (v7, made in code); claim rows take an INTEGER autoincrement key;
-- instants as INTEGER (DateTimeOffset binary values, so ordering and ranges match PostgreSQL); booleans as INTEGER;
-- byte arrays as BLOB. SQLite ignores the model's lengths. No foreign keys: a user's relation rows leave with it in code.

-- ── Accounts ────────────────────────────────────────────────────────────────────────────────────────────────────────

CREATE TABLE identity_users (
    id                     TEXT    NOT NULL,
    user_name              TEXT    NULL,
    normalized_user_name   TEXT    NULL,
    email                  TEXT    NULL,
    normalized_email       TEXT    NULL,
    email_confirmed        INTEGER NOT NULL,
    password_hash          TEXT    NULL,
    security_stamp         TEXT    NULL,
    concurrency_stamp      TEXT    NULL,
    phone_number           TEXT    NULL,
    phone_number_confirmed INTEGER NOT NULL,
    two_factor_enabled     INTEGER NOT NULL,
    lockout_end            INTEGER NULL,
    lockout_enabled        INTEGER NOT NULL,
    access_failed_count    INTEGER NOT NULL,
    created_at             INTEGER NOT NULL,
    updated_at             INTEGER NOT NULL,
    CONSTRAINT pk_identity_users PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_users_normalized_user_name ON identity_users (normalized_user_name);
CREATE INDEX ix_identity_users_normalized_email ON identity_users (normalized_email);
CREATE INDEX ix_identity_users_phone_number ON identity_users (phone_number);

CREATE TABLE identity_roles (
    id                TEXT    NOT NULL,
    name              TEXT    NULL,
    normalized_name   TEXT    NULL,
    concurrency_stamp TEXT    NULL,
    created_at        INTEGER NOT NULL,
    updated_at        INTEGER NOT NULL,
    CONSTRAINT pk_identity_roles PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_roles_normalized_name ON identity_roles (normalized_name);

CREATE TABLE identity_user_roles (
    user_id TEXT NOT NULL,
    role_id TEXT NOT NULL,
    CONSTRAINT pk_identity_user_roles PRIMARY KEY (user_id, role_id)
);

CREATE TABLE identity_user_claims (
    id          INTEGER NOT NULL CONSTRAINT pk_identity_user_claims PRIMARY KEY AUTOINCREMENT,
    user_id     TEXT    NOT NULL,
    claim_type  TEXT    NULL,
    claim_value TEXT    NULL
);

CREATE INDEX ix_identity_user_claims_user_id ON identity_user_claims (user_id);

CREATE TABLE identity_role_claims (
    id          INTEGER NOT NULL CONSTRAINT pk_identity_role_claims PRIMARY KEY AUTOINCREMENT,
    role_id     TEXT    NOT NULL,
    claim_type  TEXT    NULL,
    claim_value TEXT    NULL
);

CREATE INDEX ix_identity_role_claims_role_id ON identity_role_claims (role_id);

CREATE TABLE identity_user_logins (
    login_provider        TEXT NOT NULL,
    provider_key          TEXT NOT NULL,
    provider_display_name TEXT NULL,
    user_id               TEXT NOT NULL,
    CONSTRAINT pk_identity_user_logins PRIMARY KEY (login_provider, provider_key)
);

CREATE INDEX ix_identity_user_logins_user_id ON identity_user_logins (user_id);

CREATE TABLE identity_user_tokens (
    user_id        TEXT NOT NULL,
    login_provider TEXT NOT NULL,
    name           TEXT NOT NULL,
    value          TEXT NULL,
    CONSTRAINT pk_identity_user_tokens PRIMARY KEY (user_id, login_provider, name)
);

-- ── Credentials ─────────────────────────────────────────────────────────────────────────────────────────────────────

CREATE TABLE identity_refresh_tokens (
    id             TEXT    NOT NULL,
    user_id        TEXT    NOT NULL,
    family_id      TEXT    NOT NULL,
    token_hash     TEXT    NOT NULL,
    security_stamp TEXT    NULL,
    created_at     INTEGER NOT NULL,
    expires_at     INTEGER NOT NULL,
    consumed_at    INTEGER NULL,
    revoked_at     INTEGER NULL,
    CONSTRAINT pk_identity_refresh_tokens PRIMARY KEY (id)
);

CREATE INDEX ix_identity_refresh_tokens_user_id ON identity_refresh_tokens (user_id);
CREATE INDEX ix_identity_refresh_tokens_family_id ON identity_refresh_tokens (family_id);

CREATE TABLE identity_passkeys (
    id            TEXT    NOT NULL,
    user_id       TEXT    NOT NULL,
    credential_id BLOB    NOT NULL,
    public_key    BLOB    NOT NULL,
    user_handle   BLOB    NOT NULL,
    sign_count    INTEGER NOT NULL,
    aa_guid       TEXT    NOT NULL,
    name          TEXT    NOT NULL,
    created_at    INTEGER NOT NULL,
    last_used_at  INTEGER NULL,
    CONSTRAINT pk_identity_passkeys PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_passkeys_credential_id ON identity_passkeys (credential_id);
CREATE INDEX ix_identity_passkeys_user_id ON identity_passkeys (user_id);

-- ── Organizations ───────────────────────────────────────────────────────────────────────────────────────────────────

CREATE TABLE identity_organizations (
    id         TEXT    NOT NULL,
    name       TEXT    NOT NULL,
    slug       TEXT    NOT NULL,
    created_at INTEGER NOT NULL,
    CONSTRAINT pk_identity_organizations PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_organizations_slug ON identity_organizations (slug);

CREATE TABLE identity_memberships (
    organization_id TEXT    NOT NULL,
    user_id         TEXT    NOT NULL,
    role            TEXT    NOT NULL,
    created_at      INTEGER NOT NULL,
    CONSTRAINT pk_identity_memberships PRIMARY KEY (organization_id, user_id)
);

CREATE INDEX ix_identity_memberships_user_id ON identity_memberships (user_id);
