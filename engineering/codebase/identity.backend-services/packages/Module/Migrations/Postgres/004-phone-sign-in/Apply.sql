-- 004-phone-sign-in — sign-in by phone with a code the instance's Telegram bot sends: durable one-time codes shared by
-- every instance, phone numbers linked to the bot's chats, and the bot's pending start links (PostgreSQL).
--
-- IdentityDbContext maps them column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Rules as in 001: no foreign keys, instants are timestamptz, lengths are the model's. No table here is published to
-- products. Secrets never land in clear: a code, its subject and its origin are stored as keyed digests, a start token as
-- its SHA-256.

-- ── One-time codes ──────────────────────────────────────────────────────────────────────────────────────────────────

-- One row per code the OTP service made. A code verifies while consumed_at is null, expires_at lies ahead and attempts
-- stays below Identity:Otp:MaxAttempts; attempts and consumed_at change by conditional updates, so instances checking
-- the same code at once share one limit and spend it once. Rows past expiry are pruned as new ones arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_otp_codes (
    id           uuid         NOT NULL,
    subject_hash varchar(64)  NOT NULL,
    scope        varchar(100) NOT NULL,
    code_hash    varchar(64)  NOT NULL,
    origin_hash  varchar(64)  NULL,
    attempts     integer      NOT NULL,
    created_at   timestamptz  NOT NULL,
    expires_at   timestamptz  NOT NULL,
    consumed_at  timestamptz  NULL,
    CONSTRAINT pk_identity_otp_codes PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_identity_otp_codes_subject_hash_scope ON {{schema}}.identity_otp_codes (subject_hash, scope);
CREATE INDEX IF NOT EXISTS ix_identity_otp_codes_origin_hash_scope ON {{schema}}.identity_otp_codes (origin_hash, scope);
CREATE INDEX IF NOT EXISTS ix_identity_otp_codes_expires_at ON {{schema}}.identity_otp_codes (expires_at);

-- ── Telegram links ──────────────────────────────────────────────────────────────────────────────────────────────────

-- One row per phone number a person shared as their own contact in the bot: the chat the bot sends that number's codes
-- to. Unique per bot and number, so a replaced bot (Identity:Telegram:BotUserName) starts with no links.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_telegram_links (
    id               uuid        NOT NULL,
    bot_user_name    varchar(64) NOT NULL,
    phone_number     varchar(32) NOT NULL,
    chat_id          bigint      NOT NULL,
    telegram_user_id bigint      NOT NULL,
    linked_at        timestamptz NOT NULL,
    CONSTRAINT pk_identity_telegram_links PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_telegram_links_bot_user_name_phone_number
    ON {{schema}}.identity_telegram_links (bot_user_name, phone_number);

-- One row per t.me start link made for a phone number: opening it attaches the chat, and a shared contact holding the
-- same number completes it once. Rows past expiry are pruned as new ones arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_telegram_link_requests (
    id               uuid        NOT NULL,
    token_hash       varchar(64) NOT NULL,
    purpose          varchar(64) NOT NULL,
    bot_user_name    varchar(64) NOT NULL,
    phone_number     varchar(32) NOT NULL,
    origin_hash      varchar(64) NULL,
    chat_id          bigint      NULL,
    telegram_user_id bigint      NULL,
    created_at       timestamptz NOT NULL,
    expires_at       timestamptz NOT NULL,
    consumed_at      timestamptz NULL,
    CONSTRAINT pk_identity_telegram_link_requests PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_telegram_link_requests_token_hash
    ON {{schema}}.identity_telegram_link_requests (token_hash);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_link_requests_telegram_user_id
    ON {{schema}}.identity_telegram_link_requests (telegram_user_id);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_link_requests_phone_number
    ON {{schema}}.identity_telegram_link_requests (phone_number);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_link_requests_origin_hash
    ON {{schema}}.identity_telegram_link_requests (origin_hash);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_link_requests_expires_at
    ON {{schema}}.identity_telegram_link_requests (expires_at);
