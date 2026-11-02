-- 006-invitations rollback — drops the invitations table (PostgreSQL). Dev and test recovery only: every standing
-- invitation link stops working, and the record of who redeemed which goes with it; memberships stay.

DROP TABLE IF EXISTS {{schema}}.identity_invitations;
