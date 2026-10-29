import importlib.util
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("toc", HERE / "toc.py")
toc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(toc)

DOC = "# Title\n\nIntro.\n\n## First part\n\ntext\n\n### Sub: one\n\n```\n## not a heading\n```\n\n## First part\n\n## Last `code` & more\n"


class Toc(unittest.TestCase):
    def test_lists_headings_with_github_anchors_skipping_code_fences(self):
        out = toc.add_toc(DOC)
        self.assertIn("- [First part](#first-part)\n  - [Sub: one](#sub-one)\n- [First part](#first-part-1)\n- [Last `code` & more](#last-code--more)", out)
        self.assertNotIn("not a heading](", out)
        self.assertTrue(out.startswith("# Title\n\n<!-- toc -->\n## Contents\n"))

    def test_idempotent_and_refreshes_a_stale_block(self):
        once = toc.add_toc(DOC)
        self.assertEqual(toc.add_toc(once), once)
        self.assertEqual(toc.add_toc(once.replace("[Last", "[Old")), once)
        self.assertEqual(once.count(toc.START), 1)

    def test_no_title_or_no_headings_is_left_alone(self):
        self.assertEqual(toc.add_toc("no title\n## A\n"), "no title\n## A\n")
        self.assertEqual(toc.add_toc("# T\n\ntext\n"), "# T\n\ntext\n")

    def test_long_documents_list_only_top_level_headings(self):
        doc = "# T\n" + "".join(f"## S{i}\n### sub{i}\n" for i in range(toc.MAX_ENTRIES))
        out = toc.add_toc(doc)
        self.assertNotIn("  - [sub0]", out)
        self.assertIn("- [S0](#s0)", out)

    def test_every_doc_in_the_repo_is_current(self):
        self.assertEqual(toc.main(["--check"]), 0)


if __name__ == "__main__":
    unittest.main()
