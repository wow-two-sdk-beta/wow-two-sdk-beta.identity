-- 011-device-confirmations — new devices: a browser an account hasn't used waits for a trusted contact's press before a
-- sign-in by the account's own code (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only: two new tables, so existing rows stay as they are. Rules as in 001: no foreign keys, instants are
-- timestamptz. Nothing here is published to products.

-- One row per account and browser the account may sign in from with no press. device_hash is the SHA-256 of the browser's
-- __Host-identity-device cookie, so one browser may be known to several accounts and the instance holds no usable cookie.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_known_devices (
    user_id              uuid        NOT NULL,
    device_hash          varchar(64) NOT NULL,
    confirmed_by_user_id uuid        NOT NULL,
    created_at           timestamptz NOT NULL,
    CONSTRAINT pk_identity_known_devices PRIMARY KEY (user_id, device_hash)
);

-- One sign-in waiting for a contact's press, after the account's own code checked out. token_hash is the SHA-256 of the
-- token the page polls with. A new request deletes the account's open one; approved rows go when the page signs in, and
-- rows past expires_at go as new requests arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_device_confirmations (
    id                 uuid         NOT NULL,
    user_id            uuid         NOT NULL,
    device_hash        varchar(64)  NOT NULL,
    token_hash         varchar(64)  NOT NULL,
    is_approved        boolean      NULL,
    decided_by_user_id uuid         NULL,
    decided_at         timestamptz  NULL,
    created_at         timestamptz  NOT NULL,
    expires_at         timestamptz  NOT NULL,
    CONSTRAINT pk_identity_device_confirmations PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_device_confirmations_token_hash
    ON {{schema}}.identity_device_confirmations (token_hash);

CREATE INDEX IF NOT EXISTS ix_identity_device_confirmations_user_id
    ON {{schema}}.identity_device_confirmations (user_id);

CREATE INDEX IF NOT EXISTS ix_identity_device_confirmations_expires_at
    ON {{schema}}.identity_device_confirmations (expires_at);
