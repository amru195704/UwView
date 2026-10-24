#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Check generated data against the published hashes, and check result CSVs.

    python3 tools/check.py data --work WORK            # SHA256SUMS of each set vs manifests/
    python3 tools/check.py data --work WORK --rehash   # also re-read every file (slow for 100 GiB)
    python3 tools/check.py csv results/2026-11-05_mac-m4/runs.csv ...
"""
import argparse
import csv
import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import sha256_file  # noqa: E402
from run import RUN_COLUMNS, SUMMARY_COLUMNS  # noqa: E402

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read_sums(path):
    out = {}
    with open(path, encoding="utf-8") as f:
        for ln in f:
            ln = ln.rstrip("\n")
            if not ln or ln.startswith("#"):
                continue
            digest, rel = ln.split("  ", 1)
            out[rel] = digest
    return out


def tree_hash(sums, prefix):
    text = "".join("%s  %s\n" % (sums[r], r) for r in sorted(sums) if r.startswith(prefix))
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def cmd_data(a):
    bad = 0
    for name in sorted(os.listdir(a.work)):
        local = os.path.join(a.work, name, "SHA256SUMS")
        ref = os.path.join(HERE, "manifests", name + ".sha256")
        if not os.path.isfile(local):
            continue
        if not os.path.isfile(ref):
            print("%-10s no manifest in the repository" % name)
            continue
        got, want = read_sums(local), read_sums(ref)
        checked = 0
        for rel, digest in want.items():
            if rel.endswith("/"):
                if not any(r.startswith(rel) for r in got):
                    continue
                d = tree_hash(got, rel)
            elif rel in got:
                d = got[rel]
                if a.rehash:
                    d = sha256_file(os.path.join(a.work, name, *rel.split("/")))
            else:
                continue
            checked += 1
            if d != digest:
                bad += 1
                print("%-10s MISMATCH %s" % (name, rel))
        print("%-10s %d checked, %s" % (name, checked, "ok" if checked else "nothing generated from the manifest"))
    print("RESULT:", "all match" if not bad else "%d mismatches" % bad)
    return 1 if bad else 0


def cmd_csv(a):
    bad = 0
    for p in a.files:
        with open(p, encoding="utf-8", newline="") as f:
            r = csv.reader(f)
            head = next(r)
            want = RUN_COLUMNS if head == RUN_COLUMNS else SUMMARY_COLUMNS
            if head != want:
                print("%s: unknown header" % p)
                bad += 1
                continue
            for i, row in enumerate(r, 2):
                if len(row) != len(want):
                    print("%s:%d: %d columns (expected %d)" % (p, i, len(row), len(want)))
                    bad += 1
        print("%s: %s" % (p, "ok" if not bad else "errors"))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("data")
    d.add_argument("--work", required=True)
    d.add_argument("--rehash", action="store_true")
    c = sub.add_parser("csv")
    c.add_argument("files", nargs="+")
    a = ap.parse_args()
    return cmd_data(a) if a.cmd == "data" else cmd_csv(a)


if __name__ == "__main__":
    sys.exit(main())
