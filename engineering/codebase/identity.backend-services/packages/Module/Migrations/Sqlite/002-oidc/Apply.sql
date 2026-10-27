-- 002-oidc — OpenIddict's four tables: clients, authorizations, scopes and tokens of the OpenID Connect server (SQLite).
--
-- IdentityDbContext maps them as OpenIddict's EF Core configuration does, with the identity_oidc_ prefix, snake_case
-- names and uuid v7 ids made in code; the Migrations test tier compares the two. Every form gets them; only the Server
-- package reads or writes them.
--
-- Storage, as the context maps it: uuid ids as TEXT; OpenIddict's instants are DateTime values, stored by EF as sortable
-- ISO-8601 TEXT; SQLite ignores the model's lengths. No foreign keys: application_id and authorization_id are model
-- links only.

CREATE TABLE identity_oidc_applications (
    id                        TEXT NOT NULL,
    application_type          TEXT NULL,
    client_id                 TEXT NULL,
    client_secret             TEXT NULL,
    client_type               TEXT NULL,
    concurrency_token         TEXT NULL,
    consent_type              TEXT NULL,
    display_name              TEXT NULL,
    display_names             TEXT NULL,
    json_web_key_set          TEXT NULL,
    permissions               TEXT NULL,
    post_logout_redirect_uris TEXT NULL,
    properties                TEXT NULL,
    redirect_uris             TEXT NULL,
    requirements              TEXT NULL,
    settings                  TEXT NULL,
    CONSTRAINT pk_identity_oidc_applications PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_oidc_applications_client_id ON identity_oidc_applications (client_id);

CREATE TABLE identity_oidc_authorizations (
    id                TEXT NOT NULL,
    application_id    TEXT NULL,
    concurrency_token TEXT NULL,
    creation_date     TEXT NULL,
    properties        TEXT NULL,
    scopes            TEXT NULL,
    status            TEXT NULL,
    subject           TEXT NULL,
    type              TEXT NULL,
    CONSTRAINT pk_identity_oidc_authorizations PRIMARY KEY (id)
);

CREATE INDEX ix_identity_oidc_authorizations_app_status_subject_type
    ON identity_oidc_authorizations (application_id, status, subject, type);

CREATE TABLE identity_oidc_scopes (
    id                TEXT NOT NULL,
    concurrency_token TEXT NULL,
    description       TEXT NULL,
    descriptions      TEXT NULL,
    display_name      TEXT NULL,
    display_names     TEXT NULL,
    name              TEXT NULL,
    properties        TEXT NULL,
    resources         TEXT NULL,
    CONSTRAINT pk_identity_oidc_scopes PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_identity_oidc_scopes_name ON identity_oidc_scopes (name);

CREATE TABLE identity_oidc_tokens (
    id                TEXT NOT NULL,
    application_id    TEXT NULL,
    authorization_id  TEXT NULL,
    concurrency_token TEXT NULL,
    creation_date     TEXT NULL,
    expiration_date   TEXT NULL,
    payload           TEXT NULL,
    properties        TEXT NULL,
    redemption_date   TEXT NULL,
    reference_id      TEXT NULL,
    status            TEXT NULL,
    subject           TEXT NULL,
    type              TEXT NULL,
    CONSTRAINT pk_identity_oidc_tokens PRIMARY KEY (id)
);

CREATE INDEX ix_identity_oidc_tokens_application_id_status_subject_type
    ON identity_oidc_tokens (application_id, status, subject, type);
CREATE INDEX ix_identity_oidc_tokens_authorization_id ON identity_oidc_tokens (authorization_id);
CREATE UNIQUE INDEX ix_identity_oidc_tokens_reference_id ON identity_oidc_tokens (reference_id);
