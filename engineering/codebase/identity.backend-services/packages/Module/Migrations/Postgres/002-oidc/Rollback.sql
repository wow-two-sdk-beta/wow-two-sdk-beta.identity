-- 002-oidc rollback — drops OpenIddict's four tables with their indexes (PostgreSQL). Dev and test recovery only: every
-- client registration, grant and issued token goes with them. No foreign keys, so the order is free.

DROP TABLE IF EXISTS {{schema}}.identity_oidc_tokens;
DROP TABLE IF EXISTS {{schema}}.identity_oidc_scopes;
DROP TABLE IF EXISTS {{schema}}.identity_oidc_authorizations;
DROP TABLE IF EXISTS {{schema}}.identity_oidc_applications;
