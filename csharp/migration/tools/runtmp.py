"""Per-run scratch directory for the tools (T390).

dotnet (MSBuild), the test hosts and the harness scenarios all create files and folders under $TMPDIR (default /tmp) and many
never remove them: before T390 /tmp held ~95,000 leftover entries (65,000 empty MSBuildTemp* folders, 28,000 empty 6-character
folders, thousands of unciv-* test folders). gate.py, ptest.py and release.py therefore call acquire() on start: it creates
/tmp/unciv-run-<pid>-<random>, exports it as TMPDIR (and UNCIV_RUN_TMP, so child tools reuse it instead of nesting a second one)
and release() removes it on exit, also after a failure. Keep it with UNCIV_KEEP_TMP=1 or --keep-tmp (the path is printed).
tools/reap.py --scratch lists/removes folders of runs that died without cleaning up (the pid is part of the name).
"""
import os
import shutil
import sys
import tempfile

PREFIX = "unciv-run-"
ENV_DIR = "UNCIV_RUN_TMP"
ENV_KEEP = "UNCIV_KEEP_TMP"

_owned = None
_saved = None  # (TMPDIR, tempfile.tempdir) from before acquire()


def force_rmtree(path):
    """shutil.rmtree that also removes read-only folders (a test leaves dr-x folders behind on purpose, which plain rmtree cannot enter)."""
    top = os.path.abspath(path)

    def fix(func, p, _exc):
        try:
            for q in (os.path.dirname(p), p):
                # never chmod through a symlink, and never above the tree being removed
                if (q == top or q.startswith(top + os.sep)) and not os.path.islink(q):
                    os.chmod(q, 0o700)
            func(p)
        except OSError:
            pass
    for _ in range(2):  # the second pass handles folders that only became removable after their parent was fixed
        if sys.version_info >= (3, 12):
            shutil.rmtree(path, onexc=fix)
        else:
            shutil.rmtree(path, onerror=fix)
        if not os.path.exists(path):
            return


def keep_requested(argv=None):
    return os.environ.get(ENV_KEEP, "") not in ("", "0") or "--keep-tmp" in (sys.argv if argv is None else argv)


def acquire():
    """Create (or reuse the parent run's) scratch dir and point TMPDIR at it. Returns its path."""
    global _owned, _saved
    inherited = os.environ.get(ENV_DIR)
    if inherited and os.path.isdir(inherited):
        return inherited
    _saved = (os.environ.get("TMPDIR"), tempfile.tempdir)
    path = tempfile.mkdtemp(prefix="%s%d-" % (PREFIX, os.getpid()))
    _owned = path
    os.environ[ENV_DIR] = path
    os.environ["TMPDIR"] = path
    tempfile.tempdir = path
    return path


def release(keep=False):
    """Remove the dir this process created (never an inherited one). Best effort, never raises."""
    global _owned
    path, _owned = _owned, None
    if not path:
        return
    old_tmpdir, tempfile.tempdir = _saved
    os.environ.pop(ENV_DIR, None)
    if old_tmpdir is None:
        os.environ.pop("TMPDIR", None)
    else:
        os.environ["TMPDIR"] = old_tmpdir
    if keep or keep_requested():
        print("tmp: kept %s" % path, file=sys.stderr)
        return
    force_rmtree(path)
