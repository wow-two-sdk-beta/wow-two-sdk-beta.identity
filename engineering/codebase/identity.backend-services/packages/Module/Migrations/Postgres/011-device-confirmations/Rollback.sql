-- 011-device-confirmations rollback — drops the known devices and the waiting confirmations (PostgreSQL). Dev and test
-- recovery only: every browser becomes new to its accounts, and every sign-in waiting for a press has to start again.

DROP TABLE IF EXISTS {{schema}}.identity_device_confirmations;
DROP TABLE IF EXISTS {{schema}}.identity_known_devices;
