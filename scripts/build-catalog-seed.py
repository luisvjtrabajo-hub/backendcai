"""Generate an idempotent SQL catalog from the extracted workbook JSON."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
c = json.loads((root/'catalog/ranks-missions.json').read_text(encoding='utf-8'))
def q(v):
    return 'NULL' if v is None else "'"+str(v).replace("'", "''")+"'"
lines = ['-- Generated from CAI_Sistema_de_Rangos_y_Misiones.xlsx; scripts/build-catalog-seed.py.', 'BEGIN;', 'SELECT pg_advisory_xact_lock(824671230);']
for r in c['ranks']:
    lines.append(f"INSERT INTO cai_ranks(level,code,data) VALUES({r['level']},{q(r['code'])},{q(json.dumps(r,ensure_ascii=False))}::jsonb) ON CONFLICT(level) DO NOTHING;")
for h in c['milestones']:
    lines.append(f"INSERT INTO cai_milestones(code,level,points,data) VALUES({q(h['code'])},{h['level']},{h['points']},{q(json.dumps(h,ensure_ascii=False))}::jsonb) ON CONFLICT(code) DO NOTHING;")
for m in c['missions']:
    prefix=m['code'].split('-')[0]
    typ='FORMATIVA' if prefix in ['FOR','PRE','HER'] else 'ESPIRITUAL' if prefix=='VIG' else 'OPERACIONAL'
    field=prefix in ['ESC','DEB','EST']
    limit=1 if m['repeat']=='Una vez' else 4 if m['repeat']=='4 veces' else None
    vals=[q(m['code']),q(m['title']),q(m['title']),q(typ),q(c['ranks'][m['minimumLevel']-1]['code']),str(m['points']),q(m['category']),q(m['area']),q(m['evidence']),str(limit) if limit else 'NULL',str(m['monthlyCap']) if m['monthlyCap'] else 'NULL',str(field).lower(),str(m['code'] in ['VIG-02','VIG-03','VIG-05']).lower(),str(m['code'] in ['DEB-04','EST-05']).lower()]
    lines.append('INSERT INTO missions(catalog_code,title,description,mission_type,minimum_rank_code,badge_weight,category,area,evidence_requirement,repeat_limit,monthly_cap,field_mission,honor_allowed,invitation_required,monthly_once,publication_state,published_at) VALUES('+','.join(vals)+','+str(m['repeat']=='Mensual').lower()+",'PUBLISHED',now()) ON CONFLICT(catalog_code) DO NOTHING;")
lines += ["INSERT INTO schema_migrations(name) VALUES('004_catalog.sql') ON CONFLICT DO NOTHING;", 'COMMIT;']
(root/'database/004_catalog.sql').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('Catalog SQL generated.')
