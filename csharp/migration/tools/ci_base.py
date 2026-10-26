#!/usr/bin/env python3
"""Pick the coverage.py comparison base for CI (T169). Prints the arguments to pass to `coverage.py --check`:

  --since <sha>   pull request: merge-base of HEAD and origin/<base branch>; push: the commit before the push
  --all           everything else (scheduled run, manual run, first push of a branch, base not in the clone)

coverage.py defaults to merging with a local `csharp-migration` branch, which a CI clone does not have, hence this helper.
Inputs come from the environment, never from interpolated workflow text: GITHUB_EVENT_NAME, GITHUB_BASE_REF, EVENT_BEFORE.
Run from anywhere inside the checkout: python3 -I csharp/migration/tools/ci_base.py
"""
import os
import re
import subprocess
import sys

ZERO = "0" * 40
SHA = re.compile(r"^[0-9a-f]{40}$")
BRANCH = re.compile(r"^[A-Za-z0-9._/-]+$")


def git(*args):
    r = subprocess.run(["git", *args], capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else ""


def commit_exists(rev):
    return bool(git("rev-parse", "--verify", "--quiet", rev + "^{commit}"))


def pick(env):
    event = env.get("GITHUB_EVENT_NAME", "")
    if event == "pull_request":
        base = env.get("GITHUB_BASE_REF", "")
        if BRANCH.match(base) and not base.startswith("-"):
            merge_base = git("merge-base", "HEAD", "origin/" + base)
            if SHA.match(merge_base):
                return ["--since", merge_base]
    elif event == "push":
        before = env.get("EVENT_BEFORE", "")
        if SHA.match(before) and before != ZERO and commit_exists(before):
            return ["--since", before]
    return ["--all"]


if __name__ == "__main__":
    print(" ".join(pick(os.environ)))
    sys.exit(0)
