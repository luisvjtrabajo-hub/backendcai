-- Ejecutar SOLO en una base de pruebas vacía, con psql.
\set ON_ERROR_STOP on
\ir ../database/001_schema.sql

INSERT INTO users(id,email,full_name,password_hash,role) VALUES
 ('00000000-0000-0000-0000-000000000001','migration-test@example.com','Prueba migración','solo-pruebas','SOLDADO_ACTIVE');
INSERT INTO missions(id,title,description,mission_type,publication_state,created_by_user_id) VALUES
 ('00000000-0000-0000-0000-000000000002','Misión anterior','Prueba de actualización','OPERACIONAL','PUBLISHED','00000000-0000-0000-0000-000000000001');
INSERT INTO files(id,owner_user_id,name,content_type,content) VALUES
 ('00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000001','evidencia.png','image/png',decode('89504e470d0a1a0a','hex')),
 ('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000001','certificado.png','image/png',decode('89504e470d0a1a0a','hex')),
 ('00000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000001','evidencia-anterior.png','image/png',decode('89504e470d0a1a0a','hex'));
INSERT INTO certificate_reviews(user_id,certificate_number,file_id,status) VALUES
 ('00000000-0000-0000-0000-000000000001','CERT-ANTERIOR','00000000-0000-0000-0000-000000000004','APPROVED');
INSERT INTO mission_submissions(mission_id,user_id,file_id,status) VALUES
 ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000003','APPROVED');

\ir ../database/002_mission_assignments.sql
\ir ../database/002_mission_assignments.sql
DO $$
BEGIN
 IF (SELECT count(*) FROM mission_assignments) <> 1 THEN RAISE EXCEPTION 'La migración debe conservar una asignación sin duplicarla'; END IF;
 IF (SELECT status FROM api_assignments LIMIT 1) <> 'APPROVED' THEN RAISE EXCEPTION 'Se perdió el estado aprobado anterior'; END IF;
 IF (SELECT count(*) FROM files WHERE purpose='MISSION_EVIDENCE') <> 2 THEN RAISE EXCEPTION 'Las evidencias actuales y anteriores deben quedar protegidas'; END IF;
 IF (SELECT purpose FROM files WHERE id='00000000-0000-0000-0000-000000000004') <> 'CERTIFICATE' THEN RAISE EXCEPTION 'El certificado debe conservar su clasificación'; END IF;
END $$;
SELECT 'OK: actualización desde 001, reejecución sin duplicados y clasificación de archivos' AS resultado;
