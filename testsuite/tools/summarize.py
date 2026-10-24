#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Turn one or more result folders into Markdown tables (one per episode).

    python3 tools/summarize.py results/2026-11-05_mac-m4 results/2026-11-07_linux-vmware > tables.md
    python3 tools/summarize.py --failures results/*      # wrong answers only (input for weak-points.md)

Rules (docs/00-rules.md): seconds are compared only inside one machine;
two values within 1.5x are shown as "same".
"""
import argparse
import csv
import json
import os
import sys


def load(folders):
    rows, envs = [], {}
    for d in folders:
        p = os.path.join(d, "summary.csv")
        if not os.path.isfile(p):
            continue
        with open(p, encoding="utf-8") as f:
            rows += list(csv.DictReader(f))
        e = os.path.join(d, "env.json")
        if os.path.isfile(e):
            with open(e, encoding="utf-8") as f:
                env = json.load(f)
            envs[env["machine"]] = env
    return rows, envs


def ratio(a, b):
    if not a or not b:
        return ""
    a, b = float(a), float(b)
    if a <= 0 or b <= 0:
        return ""
    r = a / b
    if 1 / 1.5 < r < 1.5:
        return "same"
    return "%.2fx" % r


def human(n):
    try:
        n = int(n)
    except (TypeError, ValueError):
        return ""
    for u, v in (("GiB", 2**30), ("MiB", 2**20), ("KiB", 2**10)):
        if n >= v:
            return "%.3g %s" % (n / v, u)
    return "%d B" % n


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folders", nargs="+")
    ap.add_argument("--failures", action="store_true")
    a = ap.parse_args()
    rows, envs = load(a.folders)
    out = sys.stdout
    if a.failures:
        out.write("| machine | product | case | got | expected | note |\n|---|---|---|---|---|---|\n")
        for r in rows:
            if r["correct"] not in ("ok", "obs", "skip", "missing"):
                out.write("| %s | %s | %s | %s | %s | %s |\n" % (r["machine"], r["product"], r["case"], r["hits"],
                                                             r["expected_hits"], r["note"].replace("|", "\\|")))
        return 0
    out.write("## Machines\n\n| label | OS | CPU | cores | memory | file system | virtualization | products |\n"
              "|---|---|---|---|---|---|---|---|\n")
    for m, e in envs.items():
        prods = ", ".join("%s" % v["version"] for v in e["products"].values())
        out.write("| %s | %s | %s | %s | %s GiB | %s | %s | %s |\n" % (m, e["os_version"], e["cpu"], e["cores"],
                                                                     e["memory_gib"], e["fs"], e["virtualization"] or "-", prods))
    by_ep = {}
    for r in rows:
        by_ep.setdefault(int(r["episode"]), []).append(r)
    for ep in sorted(by_ep):
        out.write("\n## Episode %d\n\n" % ep)
        out.write("| machine | product | case | size | cache | median s | MB/s | runs | answer | hits |\n"
                  "|---|---|---|---|---|---|---|---|---|---|\n")
        rs = sorted(by_ep[ep], key=lambda r: (r["machine"], r["case"], r["product"], r["cache"]))
        for r in rs:
            out.write("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n" % (
                r["machine"], r["product"], r["case"], human(r["file_bytes"]), r["cache"], r["median_s"],
                r["mb_per_s"], r["runs"], r["correct"], r["hits"]))
        # uvf vs uvp on the same machine
        pairs = {}
        for r in rs:
            pairs.setdefault((r["machine"], r["case"], r["cache"]), {})[r["product"]] = r["median_s"]
        lines = [(k, v) for k, v in pairs.items() if "uvf" in v and "uvp" in v]
        if lines:
            out.write("\n| machine | case | cache | uvf / uvp |\n|---|---|---|---|\n")
            for (m, c, ca), v in lines:
                out.write("| %s | %s | %s | %s |\n" % (m, c, ca, ratio(v["uvf"], v["uvp"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
