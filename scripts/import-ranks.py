"""Extract the approved workbook into a reviewable catalog and its original shields.
Run from the outer cai folder using Python with openpyxl. Never modifies the workbook.
"""
import json
from pathlib import Path
import openpyxl

root = Path(__file__).resolve().parents[2]
book = openpyxl.load_workbook(root / 'CAI_Sistema_de_Rangos_y_Misiones.xlsx')
codes = ['POSTULANTE', 'COMPANERO_ARMAS', 'ESCUDERO', 'SARGENTO_ARMAS',
         'CABALLERO_TEMPLE', 'COMENDADOR', 'PRECEPTOR', 'MARISCAL', 'SENESCAL', 'GRAN_MAESTRE']
ranks = []
for row in list(book['Rangos'].values)[4:14]:
    n, name, motto, _, threshold, _, bonus, milestone, functions, shield = row
    ranks.append(dict(level=n, code=codes[n-1], name=name, motto=motto, threshold=threshold,
                      bonus=bonus, requirement=milestone, functions=functions, shieldDescription=shield,
                      shieldUrl=f'/ranks/{codes[n-1]}.png'))
missions = []
for row in list(book['Misiones'].values)[4:57]:
    code, title, category, area, points, evidence, level, repeat, cap = row
    missions.append(dict(code=code, title=title, category=category, area=area, points=points,
                         evidence=evidence, minimumLevel=level, repeat=repeat,
                         monthlyCap=cap if isinstance(cap, int) else None))
hitos = [dict(code=r[0], name=r[1], level=r[2], requirement=r[4], evidence=r[5], points=r[6] if isinstance(r[6], int) else 0)
         for r in list(book['Hitos'].values)[4:14]]
rules = [dict(code=r[0], name=r[1], requirement=r[2], enforcement=r[3])
         for r in list(book['Reglas y salvaguardas'].values)[4:16]]
out = root / 'backendcai/catalog'
out.mkdir(exist_ok=True)
(out / 'ranks-missions.json').write_text(json.dumps(dict(source=book.properties.title or 'CAI_Sistema_de_Rangos_y_Misiones.xlsx', ranks=ranks, missions=missions, milestones=hitos, rules=rules), ensure_ascii=False, indent=2), encoding='utf-8')
images = root / 'cai/public/ranks'
images.mkdir(parents=True, exist_ok=True)
for img in book['Rangos']._images:
    (images / f'{codes[img.anchor._from.row-4]}.png').write_bytes(img._data())
print(f'{len(ranks)} ranks, {len(missions)} missions, {len(hitos)} milestones, {len(rules)} rules; 10 original shields.')
