-- 004-user-soft-delete rollback — back to deleting the account row (PostgreSQL). Dev and test recovery only.
--
-- Before 004 a deleted account left no row, so the rows deleted since go first (their relation rows left when they
-- were deleted); otherwise dropping deleted_at would bring them back to life. Then both indexes cover every row again,
-- and deleted_at goes. Plain index builds: a rollback runs in one transaction, never against a live service.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = '{{schema}}' AND table_name = 'identity_users' AND column_name = 'deleted_at')
    THEN
        DELETE FROM {{schema}}.identity_users WHERE deleted_at IS NOT NULL;
    END IF;
END
$$;

DROP INDEX IF EXISTS {{schema}}.ix_identity_users_normalized_user_name_swap;
DROP INDEX IF EXISTS {{schema}}.ix_identity_users_normalized_user_name;
CREATE UNIQUE INDEX ix_identity_users_normalized_user_name ON {{schema}}.identity_users (normalized_user_name);

DROP INDEX IF EXISTS {{schema}}.ix_identity_users_normalized_email;
CREATE INDEX ix_identity_users_normalized_email ON {{schema}}.identity_users (normalized_email);

ALTER TABLE {{schema}}.identity_users DROP COLUMN IF EXISTS deleted_at;
