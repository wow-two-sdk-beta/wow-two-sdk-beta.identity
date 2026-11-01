-- 005-display-name — the name a person goes by, beside the sign-in identifiers (PostgreSQL).
--
-- IdentityDbContext maps it column for column; the Migrations test tier compares the two. {{schema}} is
-- Identity:Database:Schema (default identity).
--
-- An expand step on a published table: one nullable column, no default, so existing rows and every product's key stay as
-- they are. The OIDC name claim takes it first; a person without one falls back to the user name, email or phone.
-- Erasure scrubs it with the other personal columns. It is not one of the table's readable columns for products.

ALTER TABLE {{schema}}.identity_users ADD COLUMN IF NOT EXISTS display_name varchar(120) NULL;
