-- 008-telegram-link-changes rollback — drops the pending Telegram link changes (PostgreSQL). Dev and test recovery
-- only: every change waiting for approval goes, and its Telegram account shares the number again; the links stay.

DROP TABLE IF EXISTS {{schema}}.identity_telegram_link_changes;
