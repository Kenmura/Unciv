"""Tool tests: python3 -I -m unittest discover -s bnw-spec/tools -p 'test_*.py' (from csharp/migration)."""
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import gen_ruleset as g


class GeneratorTests(unittest.TestCase):
    def test_generated_files_are_current(self):
        files, report = g.generate()
        for name, text in g.render(files).items():
            self.assertEqual((g.OUT_DIR / name).read_text(encoding="utf-8"), text, name)
        self.assertEqual(g.REPORT.read_text(encoding="utf-8"), report.render() + "\n")

    def test_generation_is_idempotent(self):
        a = g.render(g.generate()[0])
        b = g.render(g.generate()[0])
        self.assertEqual(a, b)

    def test_output_is_strict_json(self):
        for name, text in g.render(g.generate()[0]).items():
            json.loads(text)

    def test_no_duplicate_names_and_spot_values(self):
        files = g.generate()[0]
        for name, data in files.items():
            if isinstance(data, list) and data and "name" in data[0]:
                names = [o["name"] for o in data]
                self.assertEqual(len(names), len(set(names)), name)
        units = {u["name"]: u for u in files["Units.json"]}
        self.assertEqual((units["Archer"]["strength"], units["Spearman"]["strength"]), (5, 11))
        self.assertEqual(units["Impi"]["upgradesTo"], "Rifleman")
        self.assertEqual(units["Impi"]["strength"], 16)
        buildings = {b["name"]: b for b in files["Buildings.json"]}
        self.assertEqual(buildings["Caravansary"]["cost"], 120)
        self.assertEqual(buildings["Zoo"]["maintenance"], 2)
        techs = {t["name"]: t for c in files["Techs.json"] for t in c["techs"]}
        self.assertEqual(techs["Optics"]["cost"], 85)
        self.assertEqual(sorted(techs["Ballistics"]["prerequisites"]), ["Flight", "Railroad"])
        self.assertIn("Triggers the creation of archaeological sites", techs["Archaeology"]["uniques"])
        self.assertIn("[+100]% influence spread from Tourism", techs["The Internet"]["uniques"])

    def test_trade_hooks_are_emitted_and_every_tech_column_is_classified(self):
        files = g.generate()[0]
        techs = {t["name"]: t for c in files["Techs.json"] for t in c["techs"]}
        self.assertIn("[+1] trade route slots", techs["Compass"]["uniques"])
        self.assertIn("[+10] sea trade route range", techs["Compass"]["uniques"])
        buildings = {b["name"]: b for b in files["Buildings.json"]}
        self.assertIn("[+200] Gold (x1/100) from land trade routes [in this city]", buildings["Caravansary"]["uniques"])
        self.assertIn("[+12] Tourism <after discovering [Radio]>", buildings["Eiffel Tower"]["uniques"])
        for row in g.tsv("Technologies"):
            self.assertTrue(all(c in g.TECH_COLUMNS for c, v in row.items() if v not in ("", "0")), row["Name"])

    def test_lenient_loads(self):
        self.assertEqual(g.lenient_loads('{"a": [1, 2,], // c\n "b": "x//y" /* z */}'), {"a": [1, 2], "b": "x//y"})

    def test_renames_touch_exact_names_and_bracket_parameters_only(self):
        r = {"Railroads": "Railroad"}
        self.assertEqual(g.apply_renames({"requiredTech": "Railroads", "u": ["[Railroads] x", "Railroads gold"]}, r),
                         {"requiredTech": "Railroad", "u": ["[Railroad] x", "Railroads gold"]})

    def test_patch_operations(self):
        o = {"uniques": ["a", "b"], "x": 1}
        g.apply_patch(o, {"uniques+": ["c"], "uniques-": ["a"], "x": None})
        self.assertEqual(o, {"uniques": ["b", "c"]})


if __name__ == "__main__":
    unittest.main()
