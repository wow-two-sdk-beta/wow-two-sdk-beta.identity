-- 004-phone-sign-in rollback — drops the one-time code, Telegram link and start-link tables (PostgreSQL). Dev and test
-- recovery only: every pending code and every phone's link to the bot goes with them, so each person shares their
-- contact in the bot again. No foreign keys, so the order is free.

DROP TABLE IF EXISTS {{schema}}.identity_telegram_link_requests;
DROP TABLE IF EXISTS {{schema}}.identity_telegram_links;
DROP TABLE IF EXISTS {{schema}}.identity_otp_codes;
