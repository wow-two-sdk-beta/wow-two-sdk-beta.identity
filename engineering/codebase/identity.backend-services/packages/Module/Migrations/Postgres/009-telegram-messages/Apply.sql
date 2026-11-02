-- 009-telegram-messages — the ledger of the messages the instance's Telegram bot sent and must clean up, and the inline
-- buttons they carry (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only: two new tables, not published to products. Rules as in 001: no foreign keys, instants are
-- timestamptz, lengths are the model's. A button's callback data never lands in clear, only its SHA-256.

-- One row per code or notice the bot sent: once delete_at passes, the minute sweep deletes the message (sent under 48
-- hours ago, as Telegram allows) or edits it to a one-line stub with no buttons (48 hours or older), then drops the row.
-- A new notice of a kind for a person makes that person's older notices of the kind in the chat due at once.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_telegram_messages (
    id            uuid        NOT NULL,
    bot_user_name varchar(64) NOT NULL,
    chat_id       bigint      NOT NULL,
    message_id    integer     NOT NULL,
    kind          varchar(64) NOT NULL,
    user_id       uuid        NULL,
    culture       varchar(16) NULL,
    sent_at       timestamptz NOT NULL,
    delete_at     timestamptz NOT NULL,
    CONSTRAINT pk_identity_telegram_messages PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_telegram_messages_bot_user_name_chat_id_message_id
    ON {{schema}}.identity_telegram_messages (bot_user_name, chat_id, message_id);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_messages_user_id_kind
    ON {{schema}}.identity_telegram_messages (user_id, kind);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_messages_delete_at
    ON {{schema}}.identity_telegram_messages (delete_at);

-- One row per inline button on a recorded message: its callback data is an opaque random token stored as its SHA-256,
-- pressed once and only by allowed_telegram_user_id; pressing one spends every button of the message. Rows go with their
-- message's row.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_telegram_callbacks (
    id                       uuid         NOT NULL,
    message_record_id        uuid         NOT NULL,
    token_hash               varchar(64)  NOT NULL,
    action                   varchar(64)  NOT NULL,
    payload                  varchar(256) NULL,
    allowed_telegram_user_id bigint       NOT NULL,
    used_at                  timestamptz  NULL,
    CONSTRAINT pk_identity_telegram_callbacks PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_telegram_callbacks_token_hash
    ON {{schema}}.identity_telegram_callbacks (token_hash);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_callbacks_message_record_id
    ON {{schema}}.identity_telegram_callbacks (message_record_id);
