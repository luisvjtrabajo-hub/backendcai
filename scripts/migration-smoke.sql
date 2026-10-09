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
\ir ../database/003_rank_system.sql
\ir ../database/004_catalog.sql
-- This models a newly earned rank; reapplying must preserve it.
UPDATE users SET rank_code='ESCUDERO' WHERE id='00000000-0000-0000-0000-000000000001';
\ir ../database/003_rank_system.sql
\ir ../database/004_catalog.sql
DO $$
BEGIN
 IF (SELECT count(*) FROM cai_ranks)<>10 THEN RAISE EXCEPTION 'Faltan rangos'; END IF;
 IF (SELECT count(*) FROM cai_milestones)<>10 THEN RAISE EXCEPTION 'Faltan hitos'; END IF;
 IF (SELECT count(*) FROM missions WHERE catalog_code IS NOT NULL)<>53 THEN RAISE EXCEPTION 'Catálogo incorrecto'; END IF;
 IF (SELECT sum(points) FROM point_ledger)<>1 THEN RAISE EXCEPTION 'Se perdieron o duplicaron puntos antiguos'; END IF;
 IF (SELECT rank_code FROM users LIMIT 1)<>'ESCUDERO' THEN RAISE EXCEPTION 'La reejecución no debe reiniciar rangos nuevos'; END IF;
 IF (SELECT legacy_rank_code FROM users LIMIT 1)<>'RECRUTA' THEN RAISE EXCEPTION 'Se perdió el rango antiguo para auditoría'; END IF;
 IF (SELECT count(*) FROM mission_assignments)<>1 THEN RAISE EXCEPTION 'Se alteraron asignaciones antiguas'; END IF;
 IF NOT EXISTS(SELECT 1 FROM missions WHERE id='00000000-0000-0000-0000-000000000002' AND field_mission AND minimum_rank_code='COMPANERO_ARMAS') THEN RAISE EXCEPTION 'Las misiones operacionales antiguas también requieren las reglas de campo'; END IF;
END $$;
SELECT 'OK: sistema de rangos, datos antiguos conservados y migraciones repetibles' AS resultado;
-- Simulate a database that recorded 003 but lacks the legacy column.
ALTER TABLE users DROP COLUMN legacy_rank_code;
\ir ../database/006_users_legacy_rank_code.sql
DO $$
BEGIN
 IF (SELECT legacy_rank_code FROM users LIMIT 1) IS NOT NULL THEN RAISE EXCEPTION 'No inventar un rango histórico perdido'; END IF;
 IF (SELECT rank_code FROM users LIMIT 1)<>'ESCUDERO' THEN RAISE EXCEPTION 'La reparación alteró el rango actual'; END IF;
 -- Exercise the same profile columns used by users.list.
 PERFORM json_build_object('birthDate',u.birth_date,'parentalConsent',u.parental_consent,'reserve',u.reserve,'sponsorId',u.sponsor_id,'formationStartedAt',u.formation_started_at,'legacyRankCode',u.legacy_rank_code) FROM users u;
END $$;
UPDATE users SET legacy_rank_code='RECRUTA' WHERE id='00000000-0000-0000-0000-000000000001';
\ir ../database/006_users_legacy_rank_code.sql
DO $$
BEGIN
 IF (SELECT legacy_rank_code FROM users LIMIT 1)<>'RECRUTA' THEN RAISE EXCEPTION 'La reparación sobrescribió el rango histórico'; END IF;
 IF (SELECT count(*) FROM schema_migrations WHERE name='006_users_legacy_rank_code.sql')<>1 THEN RAISE EXCEPTION 'La reparación debe registrarse una sola vez'; END IF;
 IF (SELECT count(*) FROM mission_assignments)<>1 OR (SELECT sum(points) FROM point_ledger)<>1 THEN RAISE EXCEPTION 'La reparación alteró el historial'; END IF;
END $$;
SELECT 'OK: columna histórica reparada, consulta de perfil válida y valores conservados al repetir' AS resultado;
\ir ../database/005_mission_deletion.sql
UPDATE missions SET deleted_at=now(),publication_state='ARCHIVED' WHERE catalog_code='PRX-01';
\ir ../database/004_catalog.sql
\ir ../database/005_mission_deletion.sql
DO $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM missions WHERE catalog_code='PRX-01' AND deleted_at IS NOT NULL) THEN RAISE EXCEPTION 'Importar el catálogo revivió una misión eliminada'; END IF;
 IF EXISTS(SELECT 1 FROM api_missions v JOIN missions m ON m.id=v.id WHERE m.catalog_code='PRX-01') THEN RAISE EXCEPTION 'El catálogo muestra una misión eliminada'; END IF;
 IF (SELECT count(*) FROM mission_assignments)<>1 OR (SELECT sum(points) FROM point_ledger)<>1 THEN RAISE EXCEPTION 'Eliminar alteró el historial'; END IF;
END $$;
SELECT 'OK: eliminación lógica, catálogo sin restauraciones y migración 005 repetible' AS resultado;
