-- Run after seeding the two local activity fixtures and restarting the API.
\set ON_ERROR_STOP on
DO $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM activity_alerts a JOIN users u ON u.id=a.user_id WHERE u.email='activity60@example.com' AND a.days_inactive>=60 AND NOT u.reserve AND u.rank_code='ESCUDERO') THEN RAISE EXCEPTION '60-day alert failed'; END IF;
 IF NOT EXISTS(SELECT 1 FROM activity_alerts a JOIN users u ON u.id=a.user_id WHERE u.email='activity120@example.com' AND a.days_inactive>=120 AND u.reserve AND u.rank_code='SARGENTO_ARMAS' AND (SELECT sum(points) FROM point_ledger WHERE user_id=u.id)=500) THEN RAISE EXCEPTION '120-day reserve must preserve rank and points'; END IF;
END $$;
SELECT 'OK: 60-day alert and 120-day reserve with rank and points preserved' AS result;
