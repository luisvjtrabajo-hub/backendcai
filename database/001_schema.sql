-- PostgreSQL 16+. Ejecutar dentro de la base creada en Render.
-- Idempotente y transaccional; no contiene contraseñas ni borra datos.
BEGIN;
SELECT pg_advisory_xact_lock(824671230);
CREATE TABLE IF NOT EXISTS users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email varchar(254) NOT NULL UNIQUE CHECK (email = lower(email)),
    full_name varchar(160) NOT NULL,
    password_hash text NOT NULL,
    role varchar(30) NOT NULL DEFAULT 'SOLDADO_PENDING'
      CHECK (role IN ('SUPER_ADMIN','REGISTRADOR','SOLDADO_PENDING','SOLDADO_ACTIVE','SOLDADO_INACTIVE')),
    rank_code varchar(30) NOT NULL DEFAULT 'RECRUTA',
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS sessions (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_sessions_user ON sessions(user_id);
CREATE INDEX IF NOT EXISTS ix_sessions_expiry ON sessions(expires_at);
CREATE INDEX IF NOT EXISTS ix_users_role ON users(role,created_at DESC,id);
CREATE TABLE IF NOT EXISTS certificates (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    certificate_number varchar(100) NOT NULL UNIQUE,
    issued_to_name varchar(160),
    issued_to_email varchar(254),
    used_by_user_id uuid UNIQUE REFERENCES users(id),
    created_by_user_id uuid NOT NULL REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    used_at timestamptz
);
CREATE TABLE IF NOT EXISTS files (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_user_id uuid NOT NULL REFERENCES users(id),
    name varchar(200) NOT NULL,
    content_type varchar(80) NOT NULL,
    content bytea NOT NULL CHECK (octet_length(content) BETWEEN 1 AND 5242880),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS certificate_reviews (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES users(id),
    certificate_number varchar(100) NOT NULL,
    file_id uuid NOT NULL REFERENCES files(id),
    status varchar(20) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','REJECTED')),
    review_note varchar(2000),
    reviewed_by_user_id uuid REFERENCES users(id),
    reviewed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_reviews_one_pending ON certificate_reviews(user_id) WHERE status='PENDING';
CREATE TABLE IF NOT EXISTS missions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    title varchar(180) NOT NULL,
    description varchar(4000) NOT NULL,
    mission_type varchar(30) NOT NULL CHECK (mission_type IN ('OPERACIONAL','FORMATIVA','ESPIRITUAL')),
    minimum_rank_code varchar(30) NOT NULL DEFAULT 'RECRUTA' CHECK (minimum_rank_code IN ('RECRUTA','SOLDADO','CABO','SARGENTO')),
    gender_eligibility varchar(10) NOT NULL DEFAULT 'ALL' CHECK (gender_eligibility='ALL'),
    badge_weight integer NOT NULL DEFAULT 1 CHECK (badge_weight BETWEEN 1 AND 100),
    publication_state varchar(20) NOT NULL DEFAULT 'DRAFT' CHECK (publication_state IN ('DRAFT','PUBLISHED','ARCHIVED')),
    created_by_user_id uuid NOT NULL REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz
);
CREATE TABLE IF NOT EXISTS mission_submissions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    mission_id uuid NOT NULL REFERENCES missions(id),
    user_id uuid NOT NULL REFERENCES users(id),
    file_id uuid NOT NULL REFERENCES files(id),
    submission_note varchar(2000),
    status varchar(20) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','REJECTED')),
    review_note varchar(2000),
    reviewed_by_user_id uuid REFERENCES users(id),
    reviewed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(mission_id,user_id)
);
CREATE TABLE IF NOT EXISTS sect_reports (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sect_name varchar(180) NOT NULL,
    location_description varchar(1000) NOT NULL,
    reference_note varchar(4000) NOT NULL,
    reported_by_user_id uuid NOT NULL REFERENCES users(id),
    status varchar(20) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','APPROVED','REJECTED')),
    review_note varchar(2000),
    reviewed_by_user_id uuid REFERENCES users(id),
    approved_at timestamptz,
    reviewed_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_missions_state ON missions(publication_state,created_at DESC,id);
CREATE INDEX IF NOT EXISTS ix_submissions_user ON mission_submissions(user_id,status);
CREATE INDEX IF NOT EXISTS ix_submissions_status ON mission_submissions(status,created_at DESC,id);
CREATE INDEX IF NOT EXISTS ix_reports_status ON sect_reports(status,created_at DESC,id);
CREATE INDEX IF NOT EXISTS ix_reports_user ON sect_reports(reported_by_user_id,created_at DESC,id);
CREATE TABLE IF NOT EXISTS audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_user_id uuid NOT NULL REFERENCES users(id),
    action varchar(100) NOT NULL,
    target_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
-- Vistas con el contrato camelCase utilizado por React; jamás exponen password_hash.
CREATE OR REPLACE VIEW api_users AS
 SELECT id, email, full_name AS "fullName", role, rank_code AS "rankCode", created_at AS "createdAt",
   (SELECT file_id FROM certificate_reviews r WHERE r.user_id=u.id AND r.status='PENDING' LIMIT 1) AS "certificateFileId"
 FROM users u;
CREATE OR REPLACE VIEW api_members AS
 SELECT id,full_name AS "fullName",rank_code AS "rankCode",created_at AS "createdAt"
 FROM users WHERE role='SOLDADO_ACTIVE';
CREATE OR REPLACE VIEW api_certificates AS
 SELECT id, certificate_number AS "certificateNumber", issued_to_name AS "issuedToName",
 issued_to_email AS "issuedToEmail", (used_by_user_id IS NOT NULL) AS "isUsed",
 used_by_user_id AS "usedByUserId", created_at AS "createdAt" FROM certificates;
CREATE OR REPLACE VIEW api_missions AS
 SELECT id,title,description,mission_type AS "missionType",minimum_rank_code AS "minimumRankCode",
 gender_eligibility AS "genderEligibility",badge_weight AS "badgeWeight",publication_state AS "publicationState",
 created_at AS "createdAt",published_at AS "publishedAt" FROM missions;
CREATE OR REPLACE VIEW api_certificate_reviews AS
 SELECT r.id,r.user_id AS "userId",u.full_name AS "fullName",u.email,
 r.certificate_number AS "certificateNumber",r.file_id AS "fileId",r.status,r.review_note AS "reviewNote",
 r.created_at AS "createdAt",r.reviewed_at AS "reviewedAt"
 FROM certificate_reviews r JOIN users u ON u.id=r.user_id;
CREATE OR REPLACE VIEW api_submissions AS
 SELECT s.id,s.mission_id AS "missionId",m.title AS "missionTitle",s.user_id AS "userId",
 u.full_name AS "fullName",s.file_id AS "fileId",s.submission_note AS "submissionNote",s.status,
 s.review_note AS "reviewNote",s.created_at AS "createdAt",s.reviewed_at AS "reviewedAt"
 FROM mission_submissions s JOIN users u ON u.id=s.user_id JOIN missions m ON m.id=s.mission_id;
CREATE OR REPLACE VIEW api_sect_reports AS
 SELECT id,sect_name AS "sectName",location_description AS "locationDescription",reference_note AS "referenceNote",
 reported_by_user_id AS "reportedByUserId",status,review_note AS "reviewNote",approved_at AS "approvedAt",
 created_at AS "createdAt",reviewed_at AS "reviewedAt" FROM sect_reports;
COMMIT;
