#!/usr/bin/env python3
"""Subscription usage gate: lets a long orchestration session pause new work instead of running into a plan limit.

    usage_gate.py status            one line: "5h 3% (resets ...) | 7d 29% (resets ...)" or "Unable to retrieve"
    usage_gate.py check             exit 0 = clear to start new work, 1 = over the threshold, 2 = unable to retrieve
    usage_gate.py wait              block until clear; prints ONE line per state change (made for the Monitor tool: silent while waiting)

Defaults: threshold 98 (percent, either window), poll every 300 s (minimum 60), unknown usage counts as clear (use --strict to block).

Safety:
  * Read-only use of the login Claude Code already keeps in ~/.claude/.credentials.json (or $CLAUDE_CONFIG_DIR). It never refreshes,
    writes, or copies the token: when the access token is expired it says "Unable to retrieve" and waits for Claude Code to renew
    the file, which it re-reads on every poll.
  * The token is sent only to https://api.anthropic.com, redirects are refused (urllib would forward the header), and it is never
    printed or included in an error message.
  * The endpoint is undocumented; any failure (no file, expired token, network, HTTP error, unexpected JSON) means "Unable to retrieve".
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

URL = "https://api.anthropic.com/api/oauth/usage"
UNABLE = "Unable to retrieve"
DEFAULT_THRESHOLD = 98.0
DEFAULT_INTERVAL = 300
MIN_INTERVAL = 60
TIMEOUT_SECONDS = 20


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def credentials_path():
    return os.path.join(os.environ.get("CLAUDE_CONFIG_DIR") or os.path.expanduser("~/.claude"), ".credentials.json")


def read_token(path=None, now=None):
    """The access token, or None when missing, unreadable or expired. Never refreshes."""
    try:
        with open(path or credentials_path(), encoding="utf-8") as f:
            oauth = json.load(f)["claudeAiOauth"]
        token = oauth["accessToken"]
        expires_ms = oauth.get("expiresAt")
    except (OSError, ValueError, KeyError, TypeError):
        return None
    if not isinstance(token, str) or not token:
        return None
    if isinstance(expires_ms, (int, float)) and expires_ms / 1000 <= (time.time() if now is None else now) + 30:
        return None
    return token


def fetch_usage(token):
    """The raw usage document, or None on any failure (messages never contain the token)."""
    req = urllib.request.Request(
        URL, headers={"Authorization": "Bearer " + token, "anthropic-beta": "oauth-2025-04-20", "User-Agent": "usage-gate"}
    )
    try:
        with urllib.request.build_opener(_NoRedirect).open(req, timeout=TIMEOUT_SECONDS) as r:
            return json.loads(r.read().decode("utf-8"))
    except (urllib.error.URLError, OSError, ValueError):
        return None


def parse(doc):
    """{'5h': (percent, resets_at), '7d': (...)} or None when the document lacks either window."""
    try:
        out = {}
        for key, name in (("five_hour", "5h"), ("seven_day", "7d")):
            window = doc[key]
            out[name] = (float(window["utilization"]), window.get("resets_at"))
        return out
    except (KeyError, TypeError, ValueError):
        return None


def get_usage(fetch=fetch_usage, token_reader=read_token):
    token = token_reader()
    return None if token is None else parse(fetch(token))


def _resets(iso):
    try:
        t = datetime.fromisoformat(iso).astimezone()
    except (TypeError, ValueError):
        return "?"
    return t.strftime("%a %H:%M")


def describe(usage):
    if usage is None:
        return UNABLE
    return " | ".join(f"{n} {p:g}% (resets {_resets(r)})" for n, (p, r) in usage.items())


def blocked_by(usage, threshold):
    """The windows at or above the threshold (empty = clear). None usage is unknown, handled by the caller."""
    return [n for n, (p, _) in usage.items() if p >= threshold]


def state(usage, threshold, strict):
    """'clear', 'blocked' or 'unknown' (unknown counts as clear unless strict)."""
    if usage is None:
        return "blocked" if strict else "unknown"
    return "blocked" if blocked_by(usage, threshold) else "clear"


def _emit(line):
    print(line, flush=True)


def wait(threshold, interval, strict, get=get_usage, sleep=time.sleep, out=_emit, max_polls=None):
    """Poll until clear, printing a line only when the state changes. Returns the final state."""
    last = None
    polls = 0
    while True:
        usage = get()
        current = state(usage, threshold, strict)
        label = current if current != "blocked" or usage is None else "blocked:" + ",".join(blocked_by(usage, threshold))
        if label != last:
            out(f"{label.upper()} {describe(usage)}")
            last = label
        if current != "blocked":
            return current
        polls += 1
        if max_polls is not None and polls >= max_polls:
            return current
        sleep(interval)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["status", "check", "wait"])
    ap.add_argument("--threshold", type=float, default=DEFAULT_THRESHOLD, help="percent at which a window blocks (default 98)")
    ap.add_argument("--interval", type=int, default=DEFAULT_INTERVAL, help="seconds between polls in wait (minimum 60)")
    ap.add_argument("--strict", action="store_true", help="treat 'Unable to retrieve' as blocked instead of clear")
    args = ap.parse_args(argv)
    interval = max(MIN_INTERVAL, args.interval)
    if args.command == "status":
        print(describe(get_usage()))
        return 0
    if args.command == "check":
        usage = get_usage()
        s = state(usage, args.threshold, args.strict)
        print(("CLEAR" if s != "blocked" else "BLOCKED") + " " + describe(usage))
        return {"clear": 0, "blocked": 1, "unknown": 2}[s]
    wait(args.threshold, interval, args.strict)
    return 0


if __name__ == "__main__":
    sys.exit(main())
