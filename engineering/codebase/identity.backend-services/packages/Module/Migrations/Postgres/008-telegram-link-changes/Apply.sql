-- 008-telegram-link-changes — a Telegram link change waiting for an organization's owner or admin (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- Expand steps only: one new table, not published to products. Rules as in 001: no foreign keys, instants are
-- timestamptz, lengths are the model's.

-- One row per bot and number: another Telegram account shared a number a person holds confirmed, and the number stays
-- linked to the account it was linked from until an owner or admin of the person's organization approves the move to
-- chat_id (or denies it). A newer request replaces the row; rows a day old no longer count and are pruned as new ones
-- arrive.
CREATE TABLE IF NOT EXISTS {{schema}}.identity_telegram_link_changes (
    id               uuid        NOT NULL,
    bot_user_name    varchar(64) NOT NULL,
    phone_number     varchar(32) NOT NULL,
    chat_id          bigint      NOT NULL,
    telegram_user_id bigint      NOT NULL,
    requested_at     timestamptz NOT NULL,
    CONSTRAINT pk_identity_telegram_link_changes PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_telegram_link_changes_bot_user_name_phone_number
    ON {{schema}}.identity_telegram_link_changes (bot_user_name, phone_number);
CREATE INDEX IF NOT EXISTS ix_identity_telegram_link_changes_requested_at
    ON {{schema}}.identity_telegram_link_changes (requested_at);
