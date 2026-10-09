BEGIN;
SELECT pg_advisory_xact_lock(824671230);

-- Repair installations whose recorded rank migration predates this column.
-- Historical ranks cannot be reconstructed from the current rank: keep NULL
-- when the original value is unknown, and preserve any existing values.
ALTER TABLE users ADD COLUMN IF NOT EXISTS legacy_rank_code varchar(30);

INSERT INTO schema_migrations(name) VALUES('006_users_legacy_rank_code.sql') ON CONFLICT DO NOTHING;
COMMIT;
