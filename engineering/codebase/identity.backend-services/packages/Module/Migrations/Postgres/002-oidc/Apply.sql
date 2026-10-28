-- 002-oidc — OpenIddict's four tables: clients, authorizations, scopes and tokens of the OpenID Connect server (PostgreSQL).
--
-- IdentityDbContext maps them as OpenIddict's EF Core configuration does, with the identity_oidc_ prefix, snake_case
-- names and uuid v7 ids made in code; the Migrations test tier compares the two. Every form gets them (one schema shape
-- everywhere); only the Server package reads or writes them. {{schema}} is Identity:Database:Schema (default identity).
--
-- Rules as in 001: no foreign keys (application_id and authorization_id are model links only), instants are timestamptz,
-- lengths are OpenIddict's. client_secret holds OpenIddict's hash, never a secret; subject holds the user id (sub) as text.

CREATE TABLE IF NOT EXISTS {{schema}}.identity_oidc_applications (
    id                        uuid         NOT NULL,
    application_type          varchar(50)  NULL,
    client_id                 varchar(100) NULL,
    client_secret             text         NULL,
    client_type               varchar(50)  NULL,
    concurrency_token         varchar(50)  NULL,
    consent_type              varchar(50)  NULL,
    display_name              text         NULL,
    display_names             text         NULL,
    json_web_key_set          text         NULL,
    permissions               text         NULL,
    post_logout_redirect_uris text         NULL,
    properties                text         NULL,
    redirect_uris             text         NULL,
    requirements              text         NULL,
    settings                  text         NULL,
    CONSTRAINT pk_identity_oidc_applications PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_oidc_applications_client_id ON {{schema}}.identity_oidc_applications (client_id);

CREATE TABLE IF NOT EXISTS {{schema}}.identity_oidc_authorizations (
    id                uuid         NOT NULL,
    application_id    uuid         NULL,
    concurrency_token varchar(50)  NULL,
    creation_date     timestamptz  NULL,
    properties        text         NULL,
    scopes            text         NULL,
    status            varchar(50)  NULL,
    subject           varchar(400) NULL,
    type              varchar(50)  NULL,
    CONSTRAINT pk_identity_oidc_authorizations PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_identity_oidc_authorizations_app_status_subject_type
    ON {{schema}}.identity_oidc_authorizations (application_id, status, subject, type);

CREATE TABLE IF NOT EXISTS {{schema}}.identity_oidc_scopes (
    id                uuid         NOT NULL,
    concurrency_token varchar(50)  NULL,
    description       text         NULL,
    descriptions      text         NULL,
    display_name      text         NULL,
    display_names     text         NULL,
    name              varchar(200) NULL,
    properties        text         NULL,
    resources         text         NULL,
    CONSTRAINT pk_identity_oidc_scopes PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_oidc_scopes_name ON {{schema}}.identity_oidc_scopes (name);

CREATE TABLE IF NOT EXISTS {{schema}}.identity_oidc_tokens (
    id                uuid         NOT NULL,
    application_id    uuid         NULL,
    authorization_id  uuid         NULL,
    concurrency_token varchar(50)  NULL,
    creation_date     timestamptz  NULL,
    expiration_date   timestamptz  NULL,
    payload           text         NULL,
    properties        text         NULL,
    redemption_date   timestamptz  NULL,
    reference_id      varchar(100) NULL,
    status            varchar(50)  NULL,
    subject           varchar(400) NULL,
    type              varchar(150) NULL,
    CONSTRAINT pk_identity_oidc_tokens PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_identity_oidc_tokens_application_id_status_subject_type
    ON {{schema}}.identity_oidc_tokens (application_id, status, subject, type);
CREATE INDEX IF NOT EXISTS ix_identity_oidc_tokens_authorization_id ON {{schema}}.identity_oidc_tokens (authorization_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_identity_oidc_tokens_reference_id ON {{schema}}.identity_oidc_tokens (reference_id);
