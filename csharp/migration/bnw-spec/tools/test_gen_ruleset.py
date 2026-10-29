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
