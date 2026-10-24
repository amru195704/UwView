#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Judge a result folder again with the current right answers (cases.json from gen.py).

    python3 tools/gen.py --work ~/uwtest --set encoding --set ja-dup
    python3 tools/rejudge.py results/20261024-150931_mac-m4-32g-usbssd --work ~/uwtest --why "UTF-32 is refused"

Use it when a right answer in gen.py changes after a run (the measured exit codes, hit counts and
seconds are kept as they are). runs.csv gets the new verdicts, summary.csv and failures.csv are
written again, and env.json records when and why the folder was judged again.
Line numbers are not kept in runs.csv; a row that was wrong only because of its line numbers stays wrong.
"""
import argparse
import csv
import datetime
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run  # noqa: E402  (judge and write_summary are the same as for a fresh run)

KEEP = ("skip", "missing", "obs", "crash", "timeout")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folder", help="a result folder written by tools/run.py")
    ap.add_argument("--work", required=True, help="the folder given to gen.py (its cases.json are the right answers)")
    ap.add_argument("--why", required=True, help="why the right answers changed (written to env.json)")
    a = ap.parse_args()

    cases = {}
    for s in run.load_sets(a.work, None):
        for c in s["cases"]:
            cases[(s["set"], c["id"])] = c
    path = os.path.join(a.folder, "runs.csv")
    with open(path, encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))

    changed, missing = 0, set()
    for r in rows:
        case = cases.get((r["set"], r["case"]))
        if case is None:
            missing.add(r["set"])
            continue
        if r["correct"] in KEEP or r["exit"] in ("", None):
            continue
        exp = case.get("expect", {})
        verdict, why = run.judge(dict(case, expect={k: v for k, v in exp.items() if k != "lines"}),
                                 int(r["exit"]), int(r["hits"] or 0), [], False)
        if verdict == "ok" and "line numbers" in (r.get("note") or ""):
            verdict, why = r["correct"], r["note"]       # line numbers cannot be checked again
        note = why or case.get("note", "")               # the same as tools/run.py
        expected = exp.get("hits", exp.get("files", ""))
        if (verdict, note, str(expected)) != (r["correct"], r.get("note") or "", r.get("expected_hits") or ""):
            changed += 1
        r["correct"], r["note"], r["expected_hits"] = verdict, note, expected
    if missing:
        raise SystemExit("no cases.json for set %s under %s (run tools/gen.py --set ... first)" % (", ".join(sorted(missing)), a.work))

    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, run.RUN_COLUMNS)
        w.writeheader()
        w.writerows(rows)
    run.write_summary(a.folder, rows)

    env_path = os.path.join(a.folder, "env.json")
    with open(env_path, encoding="utf-8") as f:
        env = json.load(f)
    env.setdefault("rejudged", []).append({"date": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"),
                                           "why": a.why, "rows_changed": changed})
    with open(env_path, "w", encoding="utf-8") as f:
        json.dump(env, f, ensure_ascii=False, indent=1)
    print("%s: %d of %d rows judged differently" % (a.folder, changed, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
