-- 013-app-notices — app notices an app sends a person through the instance's Telegram bot (PostgreSQL): each app's
-- requests by idempotency key, the people who muted an app, and notices kept in the bot's message ledger.
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only: two new tables and one column made nullable, not published to products. Rules as in 001: no
-- foreign keys, instants are timestamptz, lengths are the model's. A request keeps the digest of its body, never the body.

-- A kept notice (an app's) stays in its chat: its ledger row has no cleanup time, so the sweep never finds it due.
ALTER TABLE {{schema}}.identity_telegram_messages ALTER COLUMN delete_at DROP NOT NULL;

-- One row per app and idempotency key: pending while it sends, then sent (naming the ledger row) or muted. A key's
-- first outcome answers every repeat; a repeat with another body digest is refused. Rows older than the instance's
-- idempotency window are pruned as new requests arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_app_notices (
    id                uuid         NOT NULL,
    client_id         varchar(256) NOT NULL,
    idempotency_key   varchar(128) NOT NULL,
    request_hash      varchar(64)  NOT NULL,
    user_id           uuid         NOT NULL,
    status            varchar(16)  NOT NULL,
    message_record_id uuid         NULL,
    created_at        timestamptz  NOT NULL,
    CONSTRAINT pk_identity_app_notices PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_app_notices_client_id_idempotency_key
    ON {{schema}}.identity_app_notices (client_id, idempotency_key);
CREATE INDEX IF NOT EXISTS ix_identity_app_notices_user_id
    ON {{schema}}.identity_app_notices (user_id);
CREATE INDEX IF NOT EXISTS ix_identity_app_notices_created_at
    ON {{schema}}.identity_app_notices (created_at);

-- One row per person and muted app; client_id '*' mutes every app (the bot's /stop). Codes and the bot's own security
-- notices never read it.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_notice_mutes (
    user_id   uuid         NOT NULL,
    client_id varchar(256) NOT NULL,
    muted_at  timestamptz  NOT NULL,
    CONSTRAINT pk_identity_notice_mutes PRIMARY KEY (user_id, client_id)
);
