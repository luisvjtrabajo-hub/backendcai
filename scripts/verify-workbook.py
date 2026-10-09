"""Compare the checked-in catalog with every source row, without Excel dependencies."""
import json
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[1]
ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
with zipfile.ZipFile(root.parent / "CAI_Sistema_de_Rangos_y_Misiones.xlsx") as archive:
    shared = []
    if "xl/sharedStrings.xml" in archive.namelist():
        shared = ["".join(node.itertext()) for node in ET.fromstring(archive.read("xl/sharedStrings.xml")).findall("s:si", ns)]
    rels = {node.attrib["Id"]: node.attrib["Target"] for node in ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))}
    sheets = {}
    for sheet in ET.fromstring(archive.read("xl/workbook.xml")).findall("s:sheets/s:sheet", ns):
        target = rels[sheet.attrib["{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"]]
        path = target.lstrip("/") if target.startswith("/") else "xl/" + target
        cells = {}
        for cell in ET.fromstring(archive.read(path)).findall("s:sheetData/s:row/s:c", ns):
            node = cell.find("s:v", ns)
            value = node.text if node is not None else None
            if cell.attrib.get("t") == "s":
                value = shared[int(value)]
            elif cell.attrib.get("t") == "inlineStr":
                value = "".join(cell.find("s:is", ns).itertext())
            elif value is not None:
                try:
                    value = float(value)
                except ValueError:
                    pass
            cells[cell.attrib["r"]] = value
        sheets[sheet.attrib["name"]] = cells

catalog = json.loads((root / "catalog/ranks-missions.json").read_text(encoding="utf-8"))
checks = 0

def verify(actual, expected, location):
    global checks
    if actual != expected:
        raise AssertionError(f"{location}: catalog={actual!r}, workbook={expected!r}")
    checks += 1

for collection, sheet, fields, length in [
    ("ranks", "Rangos", {"level": "A", "name": "B", "motto": "C", "threshold": "E", "bonus": "G", "requirement": "H", "functions": "I", "shieldDescription": "J"}, 10),
    ("missions", "Misiones", {"code": "A", "title": "B", "category": "C", "area": "D", "points": "E", "evidence": "F", "minimumLevel": "G", "repeat": "H", "monthlyCap": "I"}, 53),
    ("milestones", "Hitos", {"code": "A", "name": "B", "level": "C", "requirement": "E", "evidence": "F", "points": "G"}, 10),
    ("rules", "Reglas y salvaguardas", {"code": "A", "name": "B", "requirement": "C", "enforcement": "D"}, 12),
]:
    verify(len(catalog[collection]), length, collection)
    for row, item in enumerate(catalog[collection], start=5):
        for key, col in fields.items():
            value = sheets[sheet].get(f"{col}{row}")
            if key in ("monthlyCap", "points") and not isinstance(value, (int, float)):
                value = None if key == "monthlyCap" else 0
            verify(item.get(key), value, f"{sheet}!{col}{row}")
for rank in catalog["ranks"]:
    verify((root.parent / "cai/public" / rank["shieldUrl"].lstrip("/")).is_file(), True, rank["shieldUrl"])
print(f"OK: {checks} source checks; 10 ranks, 53 missions, 10 milestones, 12 rules and shields match the XLSX.")
