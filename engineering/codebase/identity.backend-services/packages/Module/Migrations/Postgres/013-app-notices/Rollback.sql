-- 013-app-notices rollback — drops app notices' requests and mutes, and the kept notices' ledger rows (PostgreSQL). Dev
-- and test recovery only: the kept notices stay in their chats, their mute buttons stop working, every mute is lost,
-- and an app's repeated idempotency key sends again.

DROP TABLE IF EXISTS {{schema}}.identity_notice_mutes;
DROP TABLE IF EXISTS {{schema}}.identity_app_notices;

DELETE FROM {{schema}}.identity_telegram_callbacks
    WHERE message_record_id IN (SELECT id FROM {{schema}}.identity_telegram_messages WHERE delete_at IS NULL);
DELETE FROM {{schema}}.identity_telegram_messages WHERE delete_at IS NULL;
ALTER TABLE {{schema}}.identity_telegram_messages ALTER COLUMN delete_at SET NOT NULL;
