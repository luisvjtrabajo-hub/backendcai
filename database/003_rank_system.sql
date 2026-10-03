-- Workbook-derived system. Existing records are retained; legacy ranks require new milestones.
BEGIN;
SELECT pg_advisory_xact_lock(824671230);
CREATE TABLE IF NOT EXISTS schema_migrations(name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS cai_ranks(level int PRIMARY KEY CHECK(level BETWEEN 1 AND 10), code varchar(30) UNIQUE NOT NULL, data jsonb NOT NULL);
CREATE TABLE IF NOT EXISTS cai_milestones(code varchar(20) PRIMARY KEY, level int UNIQUE NOT NULL, points numeric(12,2) NOT NULL, data jsonb NOT NULL);
ALTER TABLE users ADD COLUMN IF NOT EXISTS birth_date date;
ALTER TABLE users ADD COLUMN IF NOT EXISTS parental_consent boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS sponsor_id uuid REFERENCES users(id);
ALTER TABLE users ADD COLUMN IF NOT EXISTS reserve boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS activity_resumed_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE users ADD COLUMN IF NOT EXISTS formation_started_at date NOT NULL DEFAULT CURRENT_DATE;
ALTER TABLE users ADD COLUMN IF NOT EXISTS rank_migrated boolean NOT NULL DEFAULT true;
ALTER TABLE users ADD COLUMN IF NOT EXISTS legacy_rank_code varchar(30);
ALTER TABLE users ADD COLUMN IF NOT EXISTS rank_ceiling int NOT NULL DEFAULT 10 CHECK(rank_ceiling BETWEEN 1 AND 10);
UPDATE users SET legacy_rank_code=rank_code,rank_code='POSTULANTE',rank_migrated=true,formation_started_at=created_at::date,activity_resumed_at=created_at WHERE rank_code IN('RECRUTA','SOLDADO','CABO','SARGENTO');
ALTER TABLE users ALTER COLUMN rank_code SET DEFAULT 'POSTULANTE';
ALTER TABLE missions DROP CONSTRAINT IF EXISTS missions_minimum_rank_code_check;
ALTER TABLE missions DROP CONSTRAINT IF EXISTS missions_badge_weight_check;
ALTER TABLE missions ALTER COLUMN minimum_rank_code SET DEFAULT 'POSTULANTE';
ALTER TABLE missions ALTER COLUMN created_by_user_id DROP NOT NULL;
UPDATE missions SET minimum_rank_code=CASE minimum_rank_code WHEN 'RECRUTA' THEN 'POSTULANTE' WHEN 'SOLDADO' THEN 'COMPANERO_ARMAS' WHEN 'CABO' THEN 'ESCUDERO' WHEN 'SARGENTO' THEN 'SARGENTO_ARMAS' ELSE minimum_rank_code END;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS catalog_code varchar(20) UNIQUE;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS category varchar(80) NOT NULL DEFAULT 'Personalizada';
ALTER TABLE missions ADD COLUMN IF NOT EXISTS area varchar(80) NOT NULL DEFAULT 'Misiones propias';
ALTER TABLE missions ADD COLUMN IF NOT EXISTS evidence_requirement text NOT NULL DEFAULT 'Bitácora y evidencia verificable';
ALTER TABLE missions ADD COLUMN IF NOT EXISTS repeat_limit int;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS monthly_once boolean NOT NULL DEFAULT false;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS monthly_cap int;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS field_mission boolean NOT NULL DEFAULT false;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS honor_allowed boolean NOT NULL DEFAULT false;
ALTER TABLE missions ADD COLUMN IF NOT EXISTS invitation_required boolean NOT NULL DEFAULT false;
-- Older operational missions are also subject to the field safeguards.
UPDATE missions SET field_mission=true WHERE catalog_code IS NULL AND mission_type='OPERACIONAL';
UPDATE missions SET minimum_rank_code='COMPANERO_ARMAS' WHERE field_mission AND minimum_rank_code='POSTULANTE';
ALTER TABLE mission_submissions DROP CONSTRAINT IF EXISTS mission_submissions_mission_id_user_id_key;
ALTER TABLE mission_submissions ALTER COLUMN file_id DROP NOT NULL;
ALTER TABLE mission_submissions ADD COLUMN IF NOT EXISTS module_code varchar(30);
ALTER TABLE mission_submissions ADD COLUMN IF NOT EXISTS occurred_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE mission_submissions ADD COLUMN IF NOT EXISTS details jsonb NOT NULL DEFAULT '{}';
ALTER TABLE mission_submissions ADD COLUMN IF NOT EXISTS points_awarded numeric(12,2) NOT NULL DEFAULT 0;
ALTER TABLE mission_submissions ADD COLUMN IF NOT EXISTS honor_report boolean NOT NULL DEFAULT false;
CREATE UNIQUE INDEX IF NOT EXISTS ix_submission_one_pending ON mission_submissions(mission_id,user_id) WHERE status='PENDING';
CREATE TABLE IF NOT EXISTS point_ledger(
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), user_id uuid NOT NULL REFERENCES users(id),
 source_key text NOT NULL, kind varchar(30) NOT NULL, points numeric(12,2) NOT NULL,
 submission_id uuid REFERENCES mission_submissions(id), description text NOT NULL,
 earned_at timestamptz NOT NULL DEFAULT now(), created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(user_id,source_key));
INSERT INTO point_ledger(user_id,source_key,kind,points,submission_id,description,earned_at)
 SELECT s.user_id,'legacy:'||s.id,'LEGACY',m.badge_weight,s.id,'Misión aprobada antes del sistema de rangos',coalesce(s.reviewed_at,s.created_at)
 FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.status='APPROVED' AND m.catalog_code IS NULL AND NOT EXISTS(SELECT 1 FROM point_ledger l WHERE l.submission_id=s.id AND l.kind IN('MISSION','LEGACY')) ON CONFLICT DO NOTHING;
UPDATE mission_submissions s SET points_awarded=l.points FROM point_ledger l WHERE l.submission_id=s.id AND l.kind='LEGACY' AND s.points_awarded=0;
CREATE TABLE IF NOT EXISTS user_milestones(user_id uuid NOT NULL REFERENCES users(id), code varchar(20) NOT NULL REFERENCES cai_milestones(code),
 validated_by uuid REFERENCES users(id), file_id uuid REFERENCES files(id), note text, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(user_id,code));
CREATE TABLE IF NOT EXISTS conduct_reports(id uuid PRIMARY KEY DEFAULT gen_random_uuid(), reported_by uuid NOT NULL REFERENCES users(id),
 target_id uuid NOT NULL REFERENCES users(id), note varchar(2000) NOT NULL, status varchar(20) NOT NULL DEFAULT 'PENDING',
 resolution text, reviewed_by uuid REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS activity_alerts(user_id uuid PRIMARY KEY REFERENCES users(id), last_mission_at timestamptz NOT NULL,
 days_inactive int NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
ALTER TABLE sect_reports ADD COLUMN IF NOT EXISTS latitude double precision CHECK(latitude BETWEEN -85.0511 AND 85.0511);
ALTER TABLE sect_reports ADD COLUMN IF NOT EXISTS longitude double precision CHECK(longitude BETWEEN -180 AND 180);
CREATE OR REPLACE VIEW api_points AS SELECT id,user_id AS "userId",kind,points,description,earned_at AS "earnedAt",created_at AS "createdAt" FROM point_ledger;
CREATE OR REPLACE VIEW api_conduct AS SELECT id,reported_by AS "reportedBy",target_id AS "targetId",note,status,resolution,created_at AS "createdAt" FROM conduct_reports;
CREATE OR REPLACE VIEW api_activity_alerts AS SELECT a.user_id AS id,u.full_name AS "fullName",u.rank_code AS "rankCode",u.reserve,a.last_mission_at AS "lastMissionAt",a.days_inactive AS "daysInactive",a.created_at AS "createdAt" FROM activity_alerts a JOIN users u ON u.id=a.user_id;
CREATE OR REPLACE VIEW api_assignments AS
 SELECT a.id,a.mission_id AS "missionId",m.title AS "missionTitle",a.user_id AS "userId",u.full_name AS "fullName",
 coalesce((SELECT s.status FROM mission_submissions s WHERE s.mission_id=a.mission_id AND s.user_id=a.user_id ORDER BY s.created_at DESC,s.id DESC LIMIT 1),'ASSIGNED') AS status,a.created_at AS "createdAt"
 FROM mission_assignments a JOIN missions m ON m.id=a.mission_id JOIN users u ON u.id=a.user_id;
-- Keep the original view contracts stable; modules project additional catalog fields explicitly.
INSERT INTO schema_migrations(name) VALUES('001_schema.sql'),('002_mission_assignments.sql'),('003_rank_system.sql') ON CONFLICT DO NOTHING;
COMMIT;
