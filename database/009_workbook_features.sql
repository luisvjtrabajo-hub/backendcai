BEGIN;
SELECT pg_advisory_xact_lock(824671230);
ALTER TABLE users ADD COLUMN IF NOT EXISTS city varchar(120);
ALTER TABLE certificates ADD COLUMN IF NOT EXISTS kind varchar(20) NOT NULL DEFAULT 'COURSE' CHECK(kind IN('COURSE','MILESTONE','ARMOR'));
ALTER TABLE certificates ADD COLUMN IF NOT EXISTS file_id uuid REFERENCES files(id);
CREATE TABLE IF NOT EXISTS learning_settings(
 id int PRIMARY KEY CHECK(id=1), course_url text, waiting_group_url text,
 exams jsonb NOT NULL DEFAULT '{}'::jsonb, updated_at timestamptz NOT NULL DEFAULT now());
INSERT INTO learning_settings(id) VALUES(1) ON CONFLICT DO NOTHING;
-- General honor factor from Economia de puntos, row 30. Exams and the
-- cartography entry still require their explicit platform evidence.
UPDATE missions SET honor_allowed=true WHERE catalog_code IS NOT NULL AND catalog_code NOT IN('FOR-03','FOR-04','CAR-01');
CREATE TABLE IF NOT EXISTS spiritual_records(
 user_id uuid NOT NULL REFERENCES users(id), kind varchar(10) NOT NULL CHECK(kind IN('PRAYER','ROSARY')),
 day date NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(user_id,kind,day));
INSERT INTO schema_migrations(name) VALUES('009_workbook_features.sql') ON CONFLICT DO NOTHING;
COMMIT;
