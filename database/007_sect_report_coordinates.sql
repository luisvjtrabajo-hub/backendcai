BEGIN;
SELECT pg_advisory_xact_lock(824671230);

-- Repair databases that recorded 003 before map coordinates were included.
-- Existing reports and coordinates are preserved; unknown locations stay NULL.
ALTER TABLE sect_reports ADD COLUMN IF NOT EXISTS latitude double precision CHECK(latitude BETWEEN -85.0511 AND 85.0511);
ALTER TABLE sect_reports ADD COLUMN IF NOT EXISTS longitude double precision CHECK(longitude BETWEEN -180 AND 180);

INSERT INTO schema_migrations(name) VALUES('007_sect_report_coordinates.sql') ON CONFLICT DO NOTHING;
COMMIT;
