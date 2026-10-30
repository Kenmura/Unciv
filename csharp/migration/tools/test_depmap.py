"""Tests for the DEPENDENCY_MAP.md renderer (T358). Run: python3 -I -m unittest discover -s tools -p 'test_*.py'."""
import importlib.util
import re
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("ticket", Path(__file__).resolve().parent / "ticket.py")
ticket = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ticket)


class DepMap(unittest.TestCase):
    def test_diagrams_stay_small_and_linked(self):
        text = ticket.render_map()
        blocks = re.findall(r"```mermaid\n(flowchart.*?)```", text, re.S)
        for b in blocks:
            nodes = re.findall(r'^  (\w+)\["', b, re.M)
            self.assertTrue(re.search(r"-->", b), "a diagram must contain an edge")
            self.assertLess(len(nodes), 25)
            self.assertNotIn("subgraph", b)
        self.assertIn("## Ready and on deck", text)
        self.assertIn("| bright green, thick border |", text)


if __name__ == "__main__":
    unittest.main()
