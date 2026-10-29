import json
import os
import tempfile
import time
import unittest

import usage_gate as u

DOC = {
    "five_hour": {"utilization": 3.0, "resets_at": "2026-10-29T20:00:00+00:00"},
    "seven_day": {"utilization": 29.0, "resets_at": "2026-11-05T00:00:00+00:00"},
}


def doc(five, seven):
    return {"five_hour": {"utilization": five, "resets_at": None}, "seven_day": {"utilization": seven, "resets_at": None}}


class ReadToken(unittest.TestCase):
    def write(self, payload):
        d = tempfile.mkdtemp()
        p = os.path.join(d, ".credentials.json")
        with open(p, "w", encoding="utf-8") as f:
            f.write(payload if isinstance(payload, str) else json.dumps(payload))
        return p

    def test_valid_token(self):
        p = self.write({"claudeAiOauth": {"accessToken": "t", "expiresAt": (time.time() + 3600) * 1000}})
        self.assertEqual(u.read_token(p), "t")

    def test_expired_token_is_not_used_or_refreshed(self):
        p = self.write({"claudeAiOauth": {"accessToken": "t", "expiresAt": (time.time() - 5) * 1000}})
        self.assertIsNone(u.read_token(p))

    def test_token_about_to_expire_is_not_used(self):
        p = self.write({"claudeAiOauth": {"accessToken": "t", "expiresAt": (time.time() + 10) * 1000}})
        self.assertIsNone(u.read_token(p))

    def test_missing_malformed_or_empty(self):
        self.assertIsNone(u.read_token("/nonexistent/.credentials.json"))
        self.assertIsNone(u.read_token(self.write("not json")))
        self.assertIsNone(u.read_token(self.write({"other": 1})))
        self.assertIsNone(u.read_token(self.write({"claudeAiOauth": {"accessToken": ""}})))

    def test_config_dir_override(self):
        old = os.environ.get("CLAUDE_CONFIG_DIR")
        os.environ["CLAUDE_CONFIG_DIR"] = "/x"
        try:
            self.assertEqual(u.credentials_path(), "/x/.credentials.json")
        finally:
            if old is None:
                del os.environ["CLAUDE_CONFIG_DIR"]
            else:
                os.environ["CLAUDE_CONFIG_DIR"] = old


class Parse(unittest.TestCase):
    def test_parse(self):
        self.assertEqual(u.parse(DOC)["5h"][0], 3.0)
        self.assertEqual(u.parse(DOC)["7d"][0], 29.0)

    def test_unexpected_documents_are_unknown(self):
        for bad in (None, {}, {"five_hour": None}, {"five_hour": {"utilization": "x"}, "seven_day": {}}, []):
            self.assertIsNone(u.parse(bad))

    def test_get_usage_without_token_never_fetches(self):
        def boom(_):
            raise AssertionError("must not fetch without a token")

        self.assertIsNone(u.get_usage(fetch=boom, token_reader=lambda: None))
        self.assertIsNone(u.get_usage(fetch=lambda t: None, token_reader=lambda: "t"))

    def test_describe(self):
        self.assertEqual(u.describe(None), "Unable to retrieve")
        self.assertIn("5h 3%", u.describe(u.parse(DOC)))


class Decision(unittest.TestCase):
    def test_threshold_is_inclusive_on_either_window(self):
        self.assertEqual(u.state(u.parse(doc(97.9, 50)), 98, False), "clear")
        self.assertEqual(u.state(u.parse(doc(98, 50)), 98, False), "blocked")
        self.assertEqual(u.state(u.parse(doc(10, 99)), 98, False), "blocked")
        self.assertEqual(u.blocked_by(u.parse(doc(99, 99)), 98), ["5h", "7d"])

    def test_unknown_is_clear_unless_strict(self):
        self.assertEqual(u.state(None, 98, False), "unknown")
        self.assertEqual(u.state(None, 98, True), "blocked")

    def test_check_exit_codes(self):
        for usage, strict, code in ((doc(1, 1), False, 0), (doc(99, 1), False, 1), (None, False, 2), (None, True, 1)):
            parsed = None if usage is None else u.parse(usage)
            self.assertEqual({"clear": 0, "blocked": 1, "unknown": 2}[u.state(parsed, 98, strict)], code)


class Wait(unittest.TestCase):
    def run_wait(self, sequence, strict=False, max_polls=None):
        it = iter(sequence)
        lines, sleeps = [], []
        result = u.wait(98, 300, strict, get=lambda: next(it), sleep=sleeps.append, out=lines.append, max_polls=max_polls)
        return result, lines, sleeps

    def test_silent_while_blocked_and_prints_each_change_once(self):
        seq = [u.parse(doc(99, 1)), u.parse(doc(99.5, 1)), u.parse(doc(99, 99)), u.parse(doc(1, 1))]
        result, lines, sleeps = self.run_wait(seq)
        self.assertEqual(result, "clear")
        self.assertEqual(sleeps, [300, 300, 300])
        self.assertEqual([l.split()[0] for l in lines], ["BLOCKED:5H", "BLOCKED:5H,7D", "CLEAR"])

    def test_clear_returns_immediately_with_one_line(self):
        result, lines, sleeps = self.run_wait([u.parse(doc(1, 1))])
        self.assertEqual((result, len(lines), sleeps), ("clear", 1, []))

    def test_unknown_does_not_block_by_default(self):
        result, lines, _ = self.run_wait([None])
        self.assertEqual(result, "unknown")
        self.assertIn("Unable to retrieve", lines[0])

    def test_strict_keeps_waiting_until_readable(self):
        result, lines, sleeps = self.run_wait([None, None, u.parse(doc(1, 1))], strict=True)
        self.assertEqual(result, "clear")
        self.assertEqual(sleeps, [300, 300])
        self.assertEqual(len(lines), 2)  # BLOCKED once (repeats are silent), then CLEAR

    def test_max_polls_bounds_a_blocked_wait(self):
        result, _, sleeps = self.run_wait([u.parse(doc(99, 1))] * 5, max_polls=2)
        self.assertEqual((result, len(sleeps)), ("blocked", 1))


class NoRedirect(unittest.TestCase):
    def test_redirects_are_refused(self):
        self.assertIsNone(u._NoRedirect().redirect_request(None, None, 302, "Found", {}, "https://evil.example/"))


if __name__ == "__main__":
    unittest.main()
