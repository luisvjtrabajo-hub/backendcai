BEGIN;
SELECT pg_advisory_xact_lock(824671230);

-- Repair installations whose recorded 003 migration lacks this activity field.
-- Use account creation as the baseline when no resumption date is known.
-- Adding a default first would reset every existing account's inactivity clock.
ALTER TABLE users ADD COLUMN IF NOT EXISTS activity_resumed_at timestamptz;
UPDATE users SET activity_resumed_at=created_at WHERE activity_resumed_at IS NULL;
ALTER TABLE users ALTER COLUMN activity_resumed_at SET DEFAULT now();
ALTER TABLE users ALTER COLUMN activity_resumed_at SET NOT NULL;

INSERT INTO schema_migrations(name) VALUES('008_users_activity_resumed_at.sql') ON CONFLICT DO NOTHING;
COMMIT;
