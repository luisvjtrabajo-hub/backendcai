BEGIN;
SELECT pg_advisory_xact_lock(824671230);

-- Retirar del catálogo sin destruir evidencias, puntos ni hitos históricos.
ALTER TABLE missions ADD COLUMN IF NOT EXISTS deleted_at timestamptz;
CREATE OR REPLACE VIEW api_missions AS
 SELECT id,title,description,mission_type AS "missionType",minimum_rank_code AS "minimumRankCode",
 gender_eligibility AS "genderEligibility",badge_weight AS "badgeWeight",publication_state AS "publicationState",
 created_at AS "createdAt",published_at AS "publishedAt" FROM missions WHERE deleted_at IS NULL;

INSERT INTO schema_migrations(name) VALUES('005_mission_deletion.sql') ON CONFLICT DO NOTHING;
COMMIT;
