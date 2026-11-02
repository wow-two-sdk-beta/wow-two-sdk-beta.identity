-- 009-telegram-messages rollback — drops the bot's message ledger and its buttons (PostgreSQL). Dev and test recovery
-- only: the messages already sent stay in their chats with no sweep to clean them up, and their buttons stop working.

DROP TABLE IF EXISTS {{schema}}.identity_telegram_callbacks;
DROP TABLE IF EXISTS {{schema}}.identity_telegram_messages;
