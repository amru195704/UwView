#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Run the UwView test series against data made by tools/gen.py.

    python3 tools/run.py --work WORK --label mac-m4 --uvf "$(command -v uvf)"
    python3 tools/run.py --work WORK --label mac-m4 --set huge --cache cold,hot
    python3 tools/run.py --work WORK --label win11 --uvf C:\\Tools\\uvf.exe --product uvf

Each run writes <out>/runs.csv (one row per execution, written as it goes),
<out>/summary.csv (medians), <out>/failures.csv (wrong answers) and <out>/env.json.
Timing is wall-clock from process start to exit, including writing the result
lines to a file.  See docs/00-rules.md for cold / hot and the median rule.
"""
import argparse
import csv
import datetime
import json
import os
import platform
import re
import shutil
import stat
import statistics
import subprocess
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import SUITE_VERSION, eprint  # noqa: E402

IS_WIN = os.name == "nt"
IS_MAC = sys.platform == "darwin"
RUN_COLUMNS = ["suite", "run_id", "timestamp", "machine", "os", "os_version", "fs", "product",
               "product_version", "set", "case", "episode", "file_bytes", "cache", "run", "seconds",
               "exit", "hits", "hits_reported", "expected_hits", "correct", "peak_rss_mib", "note"]
SUMMARY_COLUMNS = ["suite", "run_id", "machine", "os", "fs", "product", "product_version", "set", "case",
                   "episode", "file_bytes", "cache", "runs", "median_s", "min_s", "max_s", "mb_per_s",
                   "correct", "hits", "expected_hits", "peak_rss_mib", "note"]
LINE_RE = re.compile(rb"^(?:(.*):)?(\d+)\t")


# ====================================================================== environment
def _out(cmd):
    try:
        return subprocess.run(cmd, capture_output=True, text=True, timeout=20).stdout.strip()
    except Exception:
        return ""


def mem_total():
    try:
        if IS_MAC:
            return int(_out(["sysctl", "-n", "hw.memsize"]))
        if IS_WIN:
            import ctypes

            class MS(ctypes.Structure):
                _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                            ("ullTotalPhys", ctypes.c_ulonglong), ("a", ctypes.c_ulonglong * 6)]
            m = MS()
            m.dwLength = ctypes.sizeof(MS)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
            return m.ullTotalPhys
        with open("/proc/meminfo") as f:
            for ln in f:
                if ln.startswith("MemTotal:"):
                    return int(ln.split()[1]) * 1024
    except Exception:
        pass
    return 0


def cpu_name():
    if IS_MAC:
        return _out(["sysctl", "-n", "machdep.cpu.brand_string"])
    if IS_WIN:
        return platform.processor()
    try:
        with open("/proc/cpuinfo") as f:
            for ln in f:
                if ln.lower().startswith(("model name", "hardware")):
                    return ln.split(":", 1)[1].strip()
    except Exception:
        pass
    return platform.machine()


def fs_type(path):
    path = os.path.abspath(path)
    if IS_WIN:
        try:
            import ctypes
            buf = ctypes.create_unicode_buffer(64)
            root = os.path.splitdrive(path)[0] + "\\"
            ctypes.windll.kernel32.GetVolumeInformationW(root, None, 0, None, None, None, buf, 64)
            return buf.value
        except Exception:
            return ""
    if IS_MAC:
        best, kind = "", ""
        for ln in _out(["mount"]).splitlines():
            m = re.match(r"^.* on (.*) \(([^,)]+)", ln)
            if m and path.startswith(m.group(1)) and len(m.group(1)) > len(best):
                best, kind = m.group(1), m.group(2)
        return kind
    return _out(["stat", "-f", "-c", "%T", path])


def os_version():
    if IS_MAC:
        return "macOS " + _out(["sw_vers", "-productVersion"])
    if IS_WIN:
        return "Windows %s (build %s)" % (platform.release(), platform.version())
    name = ""
    try:
        with open("/etc/os-release") as f:
            for ln in f:
                if ln.startswith("PRETTY_NAME="):
                    name = ln.split("=", 1)[1].strip().strip('"')
    except Exception:
        pass
    return "%s, kernel %s" % (name or "Linux", platform.release())


def virt():
    if IS_MAC or IS_WIN:
        return ""
    v = _out(["systemd-detect-virt"])
    if v and v != "none":
        return v
    try:
        with open("/sys/class/dmi/id/product_name") as f:
            p = f.read().strip()
            if "VMware" in p or "Virtual" in p:
                return p
    except Exception:
        pass
    return ""


def product_version(exe):
    v = _out([exe, "--version"])
    return v.splitlines()[0] if v else ""


# ====================================================================== cache control
class Cache:
    def __init__(self, args):
        self.args = args
        self.keep = None

    def prepare(self):
        if IS_WIN:
            if not self.args.rammap:
                raise SystemExit("cold on Windows needs --rammap PATH\\RAMMap64.exe (run as administrator), "
                                 "or measure cold by hand after a restart (docs/00-rules.md)")
            return
        if os.geteuid() != 0:
            eprint("cold runs need sudo (purge / drop_caches). Enter your password once:")
            subprocess.run(["sudo", "-v"], check=True)
            self.keep = threading.Thread(target=self._keepalive, daemon=True)
            self.keep.start()

    def _keepalive(self):
        while True:
            time.sleep(60)
            subprocess.run(["sudo", "-n", "-v"], capture_output=True)

    def drop(self):
        if IS_WIN:
            r = subprocess.run([self.args.rammap, "-Es"], capture_output=True)
        else:
            subprocess.run(["sync"])
            sudo = [] if os.geteuid() == 0 else ["sudo", "-n"]
            if IS_MAC:
                r = subprocess.run(sudo + ["purge"], capture_output=True)
            else:
                r = subprocess.run(sudo + ["sh", "-c", "echo 3 > /proc/sys/vm/drop_caches"], capture_output=True)
        if r.returncode != 0:
            raise RuntimeError("could not drop the file cache: %s" % r.stderr.decode(errors="replace").strip())
        if self.args.cold_hook:
            h = subprocess.run(self.args.cold_hook, shell=True)
            if h.returncode != 0:
                raise RuntimeError("--cold-hook failed (exit %d)" % h.returncode)
        time.sleep(self.args.settle)


# ====================================================================== one execution
def peak_rss_windows(handle):
    try:
        import ctypes
        from ctypes import wintypes

        class PMC(ctypes.Structure):
            _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD),
                        ("PeakWorkingSetSize", ctypes.c_size_t), ("WorkingSetSize", ctypes.c_size_t),
                        ("a", ctypes.c_size_t * 6)]
        c = PMC()
        c.cb = ctypes.sizeof(PMC)
        if ctypes.windll.psapi.GetProcessMemoryInfo(int(handle), ctypes.byref(c), c.cb):
            return c.PeakWorkingSetSize
    except Exception:
        pass
    return None


def execute(cmd, cwd, out_path, timeout):
    """Returns (seconds, exit_code, stderr_text, peak_rss_bytes, timed_out)."""
    with open(out_path, "wb") as out:
        t0 = time.perf_counter()
        p = subprocess.Popen(cmd, cwd=cwd, stdout=out, stderr=subprocess.PIPE)
        timer = threading.Timer(timeout, p.kill)
        timer.start()
        if IS_WIN:
            err = p.stderr.read()
            p.wait()
            t1 = time.perf_counter()
            rss = peak_rss_windows(p._handle)
            code = p.returncode
        else:
            # read stderr in a thread so a chatty process cannot block
            buf = []
            th = threading.Thread(target=lambda: buf.append(p.stderr.read()))
            th.start()
            _, status, ru = os.wait4(p.pid, 0)
            t1 = time.perf_counter()
            th.join()
            err = buf[0] if buf else b""
            code = os.waitstatus_to_exitcode(status)
            p.returncode = code
            rss = ru.ru_maxrss * (1 if IS_MAC else 1024)
        timed_out = not timer.is_alive() and code != 0 and (t1 - t0) >= timeout * 0.99
        timer.cancel()
    return t1 - t0, code, err.decode("utf-8", errors="replace"), rss, timed_out


def scan_output(path):
    """Count result lines and collect line numbers without loading long lines."""
    hits = 0
    lines = []
    at_start = True
    head = b""
    with open(path, "rb") as f:
        while True:
            block = f.read(8 * 1024 * 1024)
            if not block:
                break
            parts = block.split(b"\n")
            for i, part in enumerate(parts):
                last = i == len(parts) - 1
                if at_start:
                    head = part[:200]
                elif len(head) < 200:
                    head += part[:200 - len(head)]
                at_start = False
                if not last:
                    m = LINE_RE.match(head)
                    if m:
                        hits += 1
                        if len(lines) < 100000:
                            lines.append(int(m.group(2)))
                    elif head.strip():
                        hits += 1
                    at_start = True
                    head = b""
    if not at_start and head.strip():
        m = LINE_RE.match(head)
        hits += 1
        if m and len(lines) < 100000:
            lines.append(int(m.group(2)))
    return hits, lines


def count_listed_files(path):
    n = 0
    with open(path, "rb") as f:
        for ln in f:
            if ln.strip() and not ln.startswith(b"#"):
                n += 1
    return n


REPORTED_RE = re.compile(r"(\d[\d,]*) results?")


def judge(case, code, hits, lines, timed_out):
    exp = case.get("expect", {})
    if timed_out:
        return "timeout", "no answer within the time limit"
    if code < 0 or code > 2:
        return "crash", "exit code %d" % code
    if case.get("observe"):
        return "obs", ""
    why = []
    if "exit" in exp and code != exp["exit"]:
        why.append("exit %d (expected %d)" % (code, exp["exit"]))
    if "exit_in" in exp and code not in exp["exit_in"]:
        why.append("exit %d (expected one of %s)" % (code, exp["exit_in"]))
    if "hits" in exp and hits != exp["hits"]:
        why.append("hits %d (expected %d)" % (hits, exp["hits"]))
    if "files" in exp and hits != exp["files"]:
        why.append("files %d (expected %d)" % (hits, exp["files"]))
    if exp.get("lines") is not None and "hits" in exp and hits == exp["hits"] and sorted(lines) != sorted(exp["lines"]):
        why.append("line numbers %s (expected %s)" % (sorted(lines)[:5], sorted(exp["lines"])[:5]))
    return ("ng" if why else "ok"), "; ".join(why)


# ====================================================================== special cases
class Appender(threading.Thread):
    def __init__(self, path, lines=200000):
        super().__init__(daemon=True)
        self.path = path
        self.lines = lines

    def run(self):
        with open(self.path, "ab") as f:
            for i in range(self.lines):
                f.write(b"2026-10-24 append %d %s\n" % (i, b"UWTEST-NEEDLE" if i % 10000 == 0 else b"ok"))
                if i % 1000 == 0:
                    f.flush()
                    time.sleep(0.001)


def prepare(case, root):
    """Returns (skip_reason, cleanup_fn, appender)."""
    kind = case.get("kind", "search")
    if kind == "names":
        import gen
        gen.make_names(root)
    elif kind == "noperm":
        if IS_WIN:
            return "not on Windows", None, None
        if os.geteuid() == 0:
            return "running as root: permissions do not apply", None, None
        p = os.path.join(root, case["files"])
        os.chmod(p, 0)
        return None, (lambda: os.chmod(p, stat.S_IRUSR | stat.S_IWUSR)), None
    elif kind == "symlink-loop":
        if IS_WIN:
            return "not on Windows", None, None
        d = os.path.join(root, "loop")
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "a.txt"), "wb") as f:
            f.write(b"UWTEST-NEEDLE\n")
        link = os.path.join(d, "self")
        if not os.path.islink(link):
            os.symlink(".", link)
    elif kind == "growing":
        p = os.path.join(root, case["files"])
        open(p, "wb").close()
        return None, None, Appender(p)
    return None, None, None


# ====================================================================== main loop
def load_sets(work, names):
    sets = []
    for name in sorted(os.listdir(work)):
        cj = os.path.join(work, name, "cases.json")
        if not os.path.isfile(cj) or (names and name not in names):
            continue
        with open(cj, encoding="utf-8") as f:
            sets.append(json.load(f))
    return sets


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--work", required=True, help="the folder given to gen.py")
    ap.add_argument("--label", required=True, help="machine label, e.g. mac-m4-32g-usbssd / linux-vmware-2c8g / win11-16g")
    ap.add_argument("--set", action="append", help="only these sets")
    ap.add_argument("--case", help="only case ids matching this regular expression")
    ap.add_argument("--episode", type=int, action="append", help="only these episodes")
    ap.add_argument("--product", action="append", help="uvf (default) and/or uvp")
    ap.add_argument("--uvf", default=shutil.which("uvf") or "uvf")
    ap.add_argument("--uvp", default=shutil.which("uvp") or "uvp")
    ap.add_argument("--cache", default="hot", help="hot (default), cold, or cold,hot")
    ap.add_argument("--runs", type=int, default=5, help="runs per timed case and cache (default 5)")
    ap.add_argument("--runs-big", type=int, default=3, help="runs for files of 10 GiB and more (default 3)")
    ap.add_argument("--skip-big", action="store_true", help="skip cases marked big (10 GiB and more)")
    ap.add_argument("--timeout", type=float, default=3600)
    ap.add_argument("--settle", type=float, default=2.0, help="seconds to wait after dropping the cache")
    ap.add_argument("--cold-hook", help="extra shell command after dropping the cache (e.g. purge on the VM host)")
    ap.add_argument("--rammap", help="Windows: path of RAMMap64.exe for cold runs")
    ap.add_argument("--out", help="result folder (default WORK/results/<date>_<label>)")
    a = ap.parse_args()

    products = a.product or ["uvf"]
    exes = {"uvf": a.uvf, "uvp": a.uvp}
    caches = [c.strip() for c in a.cache.split(",") if c.strip()]
    run_id = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = a.out or os.path.join(a.work, "results", "%s_%s" % (run_id, a.label))
    os.makedirs(out, exist_ok=True)
    scratch = os.path.join(out, "stdout")
    os.makedirs(scratch, exist_ok=True)

    env = {"suite": SUITE_VERSION, "run_id": run_id, "machine": a.label, "os": platform.system(),
           "os_version": os_version(), "arch": platform.machine(), "cpu": cpu_name(),
           "cores": os.cpu_count(), "memory_gib": round(mem_total() / 2**30, 1), "fs": fs_type(a.work),
           "virtualization": virt(), "python": platform.python_version(),
           "products": {p: {"path": os.path.basename(exes[p]), "version": product_version(exes[p])} for p in products},
           "caches": caches, "runs": a.runs, "runs_big": a.runs_big, "cold_hook": a.cold_hook or ""}
    with open(os.path.join(out, "env.json"), "w", encoding="utf-8") as f:
        json.dump(env, f, ensure_ascii=False, indent=1)
    eprint(json.dumps(env, ensure_ascii=False))
    for p in products:
        if not env["products"][p]["version"]:
            raise SystemExit("%s not found or not runnable: %s (use --%s PATH)" % (p, exes[p], p))

    mem = mem_total()
    cache = Cache(a)
    if "cold" in caches:
        cache.prepare()

    runs_f = open(os.path.join(out, "runs.csv"), "w", encoding="utf-8", newline="")
    rw = csv.DictWriter(runs_f, RUN_COLUMNS)
    rw.writeheader()
    rows = []
    base = {"suite": SUITE_VERSION, "run_id": run_id, "machine": a.label, "os": env["os"],
            "os_version": env["os_version"], "fs": env["fs"]}

    def record(**kw):
        r = dict(base)
        r.update(kw)
        rw.writerow(r)
        runs_f.flush()
        rows.append(r)

    sets = load_sets(a.work, a.set)
    if not sets:
        raise SystemExit("no cases.json under %s (run tools/gen.py first)" % a.work)
    pat = re.compile(a.case) if a.case else None
    total = 0
    for s in sets:
        root = os.path.join(a.work, s["set"])
        for case in s["cases"]:
            if pat and not pat.search(case["id"]):
                continue
            if a.episode and case["episode"] not in a.episode:
                continue
            if a.skip_big and case.get("big"):
                continue
            for prod in products:
                total += 1
                common = {"product": prod, "product_version": env["products"][prod]["version"], "set": s["set"],
                          "case": case["id"], "episode": case["episode"], "file_bytes": case.get("bytes", ""),
                          "expected_hits": case.get("expect", {}).get("hits", case.get("expect", {}).get("files", ""))}
                if case.get("missing"):
                    record(cache="-", run=0, correct="missing", note=case["missing"], **common)
                    continue
                skip, cleanup, appender = prepare(case, root)
                if skip:
                    record(cache="-", run=0, correct="skip", note=skip, **common)
                    continue
                if case.get("kind") == "files":
                    cmd = [exes[prod], case["files"]] + case.get("flags", [])
                else:
                    cmd = [exes[prod], case["files"], case["pattern"]] + case.get("flags", [])
                plan = []
                if case.get("timed"):
                    n = a.runs_big if case.get("big") else a.runs
                    plan.append(("first", 1))  # the first search of this file in this session, kept apart
                    for c in caches:
                        if c == "hot":
                            plan.append(("hot", 0))  # warm-up, not recorded
                        plan += [(c, i + 1) for i in range(n)]
                else:
                    plan = [("-", 1)]
                for c, i in plan:
                    if appender is not None:
                        open(os.path.join(root, case["files"]), "wb").close()
                        appender = Appender(os.path.join(root, case["files"]))
                    if c == "hot" and case.get("bytes") and mem and case["bytes"] > 0.6 * mem:
                        if i == 1:
                            record(cache="hot", run=0, correct="skip", note="file does not fit in memory: hot is the same as cold", **common)
                        continue
                    if c == "cold" or (c == "first" and "cold" in caches):
                        cache.drop()
                    if appender is not None:
                        appender.start()
                    out_path = os.path.join(scratch, "%s.%s.out" % (case["id"], prod))
                    secs, code, err, rss, tout = execute(cmd, root, out_path, a.timeout if case.get("timed") else min(a.timeout, 300))
                    if appender is not None:
                        appender.join()
                    if i == 0:
                        continue
                    if case.get("kind") == "files":
                        hits, lines = count_listed_files(out_path), []
                    else:
                        hits, lines = scan_output(out_path)
                    m = REPORTED_RE.search(err)
                    reported = int(m.group(1).replace(",", "")) if m else ""
                    verdict, why = judge(case, code, hits, lines, tout)
                    note = why or case.get("note", "")
                    if err.strip():  # kept next to stdout, not in the CSV (messages may change between versions)
                        with open(out_path[:-4] + ".err", "w", encoding="utf-8") as ef:
                            ef.write(err)
                    record(cache=c, run=i, seconds="%.4f" % secs, exit=code, hits=hits, hits_reported=reported,
                           correct=verdict, peak_rss_mib="%.1f" % (rss / 2**20) if rss else "", note=note, **common)
                    eprint("%-34s %-4s %-4s #%d %8.3fs exit=%d hits=%d %s %s" % (
                        case["id"], prod, c, i, secs, code, hits, verdict, why))
                if cleanup:
                    cleanup()
    runs_f.close()
    write_summary(out, rows)
    eprint("done: %d case x product, results in %s" % (total, out))
    return 0


def write_summary(out, rows):
    groups = {}
    for r in rows:
        key = (r["product"], r["set"], r["case"], r["cache"])
        groups.setdefault(key, []).append(r)
    summ, fails = [], []
    for key, rs in groups.items():
        r0 = rs[0]
        secs = [float(r["seconds"]) for r in rs if r.get("seconds") not in ("", None)]
        verdicts = sorted(set(r["correct"] for r in rs))
        verdict = "ok" if verdicts == ["ok"] else "/".join(verdicts)
        med = statistics.median(secs) if secs else None
        size = r0.get("file_bytes")
        rss = [float(r["peak_rss_mib"]) for r in rs if r.get("peak_rss_mib")]
        summ.append({
            "suite": r0["suite"], "run_id": r0["run_id"], "machine": r0["machine"], "os": r0["os"], "fs": r0["fs"],
            "product": r0["product"], "product_version": r0["product_version"], "set": r0["set"], "case": r0["case"],
            "episode": r0["episode"], "file_bytes": size, "cache": r0["cache"], "runs": len(secs),
            "median_s": "%.4f" % med if med is not None else "", "min_s": "%.4f" % min(secs) if secs else "",
            "max_s": "%.4f" % max(secs) if secs else "",
            "mb_per_s": "%.0f" % (int(size) / 1e6 / med) if (med and size not in ("", None) and med > 0) else "",
            "correct": verdict, "hits": rs[-1].get("hits", ""), "expected_hits": r0.get("expected_hits", ""),
            "peak_rss_mib": "%.1f" % max(rss) if rss else "", "note": rs[-1].get("note", "")})
        if any(r["correct"] in ("ng", "crash", "timeout") for r in rs):
            fails.append(summ[-1])
    for name, data in (("summary.csv", summ), ("failures.csv", fails)):
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, SUMMARY_COLUMNS)
            w.writeheader()
            w.writerows(data)


if __name__ == "__main__":
    sys.exit(main())
