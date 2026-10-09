BEGIN;
SELECT pg_advisory_xact_lock(824671230);
CREATE TABLE IF NOT EXISTS city_locations(
 id uuid PRIMARY KEY, provider_id int NOT NULL UNIQUE,
 country varchar(120) NOT NULL, country_code varchar(2) NOT NULL,
 city varchar(120) NOT NULL, region varchar(200),
 latitude double precision NOT NULL CHECK(latitude BETWEEN -90 AND 90),
 longitude double precision NOT NULL CHECK(longitude BETWEEN -180 AND 180));
ALTER TABLE users ADD COLUMN IF NOT EXISTS country varchar(120);
ALTER TABLE users ADD COLUMN IF NOT EXISTS location_id uuid REFERENCES city_locations(id);
CREATE INDEX IF NOT EXISTS users_location_active_idx ON users(location_id) WHERE role='SOLDADO_ACTIVE';
CREATE OR REPLACE VIEW api_member_locations AS
 SELECT l.id,l.country,l.city,l.region,l.latitude,l.longitude,
 count(*)::int AS "memberCount",max(u.created_at) AS "createdAt"
 FROM city_locations l JOIN users u ON u.location_id=l.id
 WHERE u.role='SOLDADO_ACTIVE'
 GROUP BY l.id;
INSERT INTO schema_migrations(name) VALUES('010_member_locations.sql') ON CONFLICT DO NOTHING;
COMMIT;
