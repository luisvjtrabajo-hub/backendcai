-- Separar asignación de la evidencia; compatible con datos existentes.
BEGIN;
SELECT pg_advisory_xact_lock(824671230);
CREATE TABLE IF NOT EXISTS mission_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    mission_id uuid NOT NULL REFERENCES missions(id),
    user_id uuid NOT NULL REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(mission_id,user_id)
);
CREATE INDEX IF NOT EXISTS ix_assignments_user ON mission_assignments(user_id,created_at DESC,id);
-- Las evidencias existentes conservan su asignación y su progreso.
INSERT INTO mission_assignments(mission_id,user_id,created_at)
 SELECT mission_id,user_id,created_at FROM mission_submissions
 ON CONFLICT(mission_id,user_id) DO NOTHING;
ALTER TABLE files ADD COLUMN IF NOT EXISTS purpose varchar(30) NOT NULL DEFAULT 'CERTIFICATE'
 CHECK (purpose IN ('CERTIFICATE','MISSION_EVIDENCE'));
-- Incluye los archivos de reenvíos anteriores: ningún archivo de misión
-- podrá descargarse por un soldado, aunque haya sido quien lo subió.
UPDATE files f SET purpose='MISSION_EVIDENCE'
 WHERE purpose<>'MISSION_EVIDENCE' AND NOT EXISTS (SELECT 1 FROM certificate_reviews r WHERE r.file_id=f.id);
CREATE OR REPLACE VIEW api_assignments AS
 SELECT a.id,a.mission_id AS "missionId",m.title AS "missionTitle",a.user_id AS "userId",
 u.full_name AS "fullName",coalesce(s.status,'ASSIGNED') AS status,a.created_at AS "createdAt"
 FROM mission_assignments a JOIN users u ON u.id=a.user_id JOIN missions m ON m.id=a.mission_id
 LEFT JOIN mission_submissions s ON s.mission_id=a.mission_id AND s.user_id=a.user_id;
CREATE OR REPLACE VIEW api_submission_status AS
 SELECT s.id,s.mission_id AS "missionId",m.title AS "missionTitle",s.user_id AS "userId",
 s.status,s.created_at AS "createdAt",s.reviewed_at AS "reviewedAt"
 FROM mission_submissions s JOIN missions m ON m.id=s.mission_id;
COMMIT;
