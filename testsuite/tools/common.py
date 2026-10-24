# SPDX-License-Identifier: MIT
"""Shared helpers for the UwView test suite (Python 3.9+, standard library only).

Everything that has to be reproducible on macOS, Linux and Windows lives here:
a version-independent PRNG, a hashing writer, and the case / manifest formats.
"""
import hashlib
import json
import os
import sys

SUITE_VERSION = "1.0"
MiB = 1024 * 1024
GiB = 1024 * MiB


# ---------------------------------------------------------------- PRNG
class Rng:
    """splitmix64. Python's own `random` is not guaranteed to give the same
    sequence on every Python version, so the suite uses this instead."""

    MASK = (1 << 64) - 1

    def __init__(self, seed):
        self.state = seed & self.MASK

    def u64(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & self.MASK
        z = self.state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & self.MASK
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & self.MASK
        return z ^ (z >> 31)

    def below(self, n):
        """Integer in [0, n). The modulo bias is < 2^-40 for the sizes used here."""
        return self.u64() % n

    def choice(self, seq):
        return seq[self.below(len(seq))]

    def shuffle(self, seq):
        for i in range(len(seq) - 1, 0, -1):
            j = self.below(i + 1)
            seq[i], seq[j] = seq[j], seq[i]


def seed_of(*parts):
    """Stable 64-bit seed from strings, so every set/size gets its own stream."""
    h = hashlib.sha256("/".join(str(p) for p in parts).encode("utf-8")).digest()
    return int.from_bytes(h[:8], "little")


# ---------------------------------------------------------------- sizes
_UNITS = {"": 1, "K": 1024, "M": MiB, "G": GiB, "T": 1024 * GiB}


def parse_size(text):
    """'64M' -> 67108864. Units are binary (KiB, MiB, GiB)."""
    t = text.strip().upper().rstrip("B").rstrip("I")
    unit = t[-1] if t and t[-1] in "KMGT" else ""
    num = t[:-1] if unit else t
    return int(float(num) * _UNITS[unit])


def parse_count(text):
    """'1e9' or '100000000' or '100M' (decimal for counts)."""
    t = text.strip().upper()
    mult = {"K": 10**3, "M": 10**6, "G": 10**9}
    if t and t[-1] in mult:
        return int(float(t[:-1]) * mult[t[-1]])
    return int(float(t))


def size_label(n):
    for unit, v in (("G", GiB), ("M", MiB), ("K", 1024)):
        if n >= v and n % v == 0:
            return "%d%s" % (n // v, unit)
    return str(n)


def count_label(n):
    for unit, v in (("G", 10**9), ("M", 10**6), ("K", 10**3)):
        if n >= v and n % v == 0:
            return "%d%s" % (n // v, unit)
    return str(n)


# ---------------------------------------------------------------- writing
class HashWriter:
    """Writes a file and its SHA-256 at the same time.
    With path=None it only hashes (used to build manifests without disk)."""

    def __init__(self, path):
        self.path = path
        self.h = hashlib.sha256()
        self.size = 0
        self.f = None
        if path is not None:
            d = os.path.dirname(path)
            if d:
                os.makedirs(d, exist_ok=True)
            self.f = open(path, "wb")

    def write(self, b):
        self.h.update(b)
        self.size += len(b)
        if self.f is not None:
            self.f.write(b)

    def close(self):
        if self.f is not None:
            self.f.close()
        return self.h.hexdigest()


def sha256_file(path, bufsize=8 * MiB):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            b = f.read(bufsize)
            if not b:
                break
            h.update(b)
    return h.hexdigest()


class SetWriter:
    """Collects the files and cases of one data set.

    Layout in the work folder:
        <work>/<set>/<files...>
        <work>/<set>/cases.json     what to run and what the right answer is
        <work>/<set>/SHA256SUMS     hashes of the generated files
    """

    def __init__(self, work, name, hash_only=False):
        self.name = name
        self.root = os.path.join(work, name)
        self.hash_only = hash_only
        self.files = []  # (relpath, sha256, size)
        self.cases = []
        if not hash_only:
            os.makedirs(self.root, exist_ok=True)

    def path(self, rel):
        return os.path.join(self.root, *rel.split("/"))

    def open(self, rel):
        return HashWriter(None if self.hash_only else self.path(rel))

    def add_file(self, rel, writer):
        digest = writer.close()
        self.files.append((rel, digest, writer.size))
        return digest

    def write_bytes(self, rel, data):
        w = self.open(rel)
        w.write(data)
        return self.add_file(rel, w)

    def case(self, **kw):
        """id, episode, files (str), pattern, flags=[], expect={}, bytes, timed, note"""
        kw.setdefault("flags", [])
        kw.setdefault("expect", {})
        kw.setdefault("timed", False)
        kw.setdefault("note", "")
        kw.setdefault("kind", "search")
        self.cases.append(kw)

    def finish(self, meta):
        sums = "".join("%s  %s\n" % (d, rel) for rel, d, _ in self.files)
        doc = {"suite": SUITE_VERSION, "set": self.name, "meta": meta,
               "files": [{"path": r, "sha256": d, "bytes": s} for r, d, s in self.files],
               "cases": self.cases}
        if not self.hash_only:
            with open(os.path.join(self.root, "SHA256SUMS"), "w", encoding="utf-8", newline="\n") as f:
                f.write(sums)
            with open(os.path.join(self.root, "cases.json"), "w", encoding="utf-8", newline="\n") as f:
                json.dump(doc, f, ensure_ascii=False, indent=1)
        return sums


def eprint(*a):
    print(*a, file=sys.stderr, flush=True)
