"""Tests for tools/ptest.py and the cache key of tools/coverage.py (faster-tests).
Run: python3 -I -m unittest discover -s tools -p 'test_*.py' (from csharp/migration)."""
import importlib.util
import os
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load(name):
    spec = importlib.util.spec_from_file_location(name, HERE / (name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


ptest = load("ptest")
coverage = load("coverage")


class Partition(unittest.TestCase):
    def test_every_class_lands_in_exactly_one_shard(self):
        classes = ["A.%d" % i for i in range(40)]
        weights = {c: float(i % 7) for i, c in enumerate(classes)}
        shards = ptest.partition(classes, weights, 6)
        self.assertEqual(sorted(sum(shards, [])), sorted(classes))

    def test_heavy_classes_are_spread_out(self):
        weights = {"Big1": 30.0, "Big2": 29.0, "Big3": 28.0, "s1": 1.0, "s2": 1.0}
        shards = ptest.partition(list(weights), weights, 3)
        self.assertEqual(sorted(len([c for c in s if c.startswith("Big")]) for s in shards), [1, 1, 1])

    def test_unknown_classes_get_a_default_weight_and_are_still_placed(self):
        shards = ptest.partition(["New.Class", "Old.Class"], {"Old.Class": 5.0}, 2)
        self.assertEqual(sorted(sum(shards, [])), ["New.Class", "Old.Class"])

    def test_filters_end_at_the_class_boundary(self):
        # "FooTests." must not also select FooTestsExtra
        self.assertEqual(ptest.class_filter(["N.FooTests", "N.Bar"]), "FullyQualifiedName~N.FooTests.|FullyQualifiedName~N.Bar.")
        self.assertEqual(ptest.remainder_filter(["N.FooTests", "N.Bar"]), "FullyQualifiedName!~N.FooTests.&FullyQualifiedName!~N.Bar.")


class Affected(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        (self.root / "Unciv.Core.Tests" / "Logic").mkdir(parents=True)
        (self.root / "Unciv.Core" / "Logic").mkdir(parents=True)
        (self.root / "Unciv.Core" / "Logic" / "Widget.cs").write_text("namespace Unciv.Logic;\npublic class Widget { }\n")
        (self.root / "Unciv.Core" / "Logic" / "Gadget.cs").write_text("namespace Unciv.Logic;\npublic class Gadget { }\n")
        t = self.root / "Unciv.Core.Tests" / "Logic"
        (t / "WidgetUserTests.cs").write_text("namespace Unciv.Core.Tests.Logic;\npublic class WidgetUserTests { [Fact] public void A() { new Widget(); } }\n")
        (t / "GadgetUserTests.cs").write_text("namespace Unciv.Core.Tests.Logic;\npublic class GadgetUserTests { [Fact] public void A() { new Gadget(); } }\n")
        (t / "UnrelatedTests.cs").write_text("namespace Unciv.Core.Tests.Logic;\npublic class UnrelatedTests { [Fact] public void A() { } }\n")
        (t / "GadgetTests.cs").write_text("namespace Unciv.Core.Tests.Logic;\npublic class GadgetTests { [Fact] public void A() { } }\n")
        self.old = ptest.CSHARP
        ptest.CSHARP = str(self.root)
        self.addCleanup(lambda: setattr(ptest, "CSHARP", self.old))

    def test_selects_only_tests_mentioning_the_changed_type_or_named_after_the_file(self):
        got, _ = ptest.affected_classes(["Unciv.Core/Logic/Gadget.cs"])
        self.assertEqual(got, {"Unciv.Core.Tests.Logic.GadgetUserTests", "Unciv.Core.Tests.Logic.GadgetTests"})

    def test_a_changed_test_file_selects_its_own_classes(self):
        got, _ = ptest.affected_classes(["Unciv.Core.Tests/Logic/UnrelatedTests.cs"])
        self.assertEqual(got, {"Unciv.Core.Tests.Logic.UnrelatedTests"})

    def test_a_non_cs_change_means_everything(self):
        self.assertIsNone(ptest.affected_classes(["Unciv.Core/Unciv.Core.csproj"])[0])
        self.assertIsNone(ptest.affected_classes(["../android/assets/jsons/Buildings.json"])[0])

    def test_docs_and_migration_files_select_nothing(self):
        self.assertEqual(ptest.affected_classes(["migration/backlog/T001.md", "README.md"])[0], set())


class CacheKey(unittest.TestCase):
    def test_key_ignores_migration_and_mtime_but_not_source_content(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "migration").mkdir()
            (root / "a.cs").write_text("class A {}")
            (root / "migration" / "x.json").write_text("{}")
            old = coverage.CSHARP, coverage.git
            coverage.CSHARP = d
            coverage.git = lambda *a: ["a.cs", "migration/x.json", "notes.md", ""]
            try:
                k1 = coverage.source_key()
                (root / "migration" / "x.json").write_text('{"changed": 1}')
                os.utime(root / "a.cs", (1, 1))
                self.assertEqual(coverage.source_key(), k1)
                (root / "a.cs").write_text("class A { int x; }")
                self.assertNotEqual(coverage.source_key(), k1)
            finally:
                coverage.CSHARP, coverage.git = old


if __name__ == "__main__":
    unittest.main()
