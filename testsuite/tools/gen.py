#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Generate the test data of the UwView test series.

    python3 tools/gen.py --work WORKDIR --preset smoke          # small, minutes
    python3 tools/gen.py --work WORKDIR --preset trial          # first trial run
    python3 tools/gen.py --work WORKDIR --preset full           # everything (needs ~250 GB)
    python3 tools/gen.py --work WORKDIR --set huge --sizes 1G,10G
    python3 tools/gen.py --list

Every byte is derived from a fixed seed, so the same command gives the same
files (and the same SHA-256) on macOS, Linux and Windows.  Each set writes
WORKDIR/<set>/cases.json (what to search and the right answer) and
WORKDIR/<set>/SHA256SUMS.  Compare it with manifests/ using tools/check.py.
"""
import argparse
import gzip
import bz2
import lzma
import io
import os
import re
import shutil
import subprocess
import sys
import tarfile
import time
import unicodedata
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (Rng, seed_of, parse_size, parse_count, size_label, count_label,
                    SetWriter, MiB, GiB, eprint)  # noqa: E402

# ====================================================================== corpus
CITIES = ["東京", "大阪", "名古屋", "札幌", "福岡", "仙台", "広島", "京都", "横浜", "神戸", "那覇", "金沢"]
LEVELS = ["INFO"] * 14 + ["WARN"] * 3 + ["DEBUG"] * 2 + ["ERROR"]
STATUS = [200] * 90 + [201, 204, 301, 304, 400, 404, 404, 500, 503, 502]
PATHS = ["items", "users", "orders", "search", "files", "maps", "points", "logs"]
WORDS = ["受付", "完了", "再送", "確認", "更新", "削除", "検索", "読込", "保存", "応答"]


def log_line(rng):
    d = 1 + rng.below(28)
    s = rng.below(86400)
    ms = rng.below(1000)
    return "2026-10-%02dT%02d:%02d:%02d.%03dZ %-5s [w%02d] %s %s id=%d status=%d bytes=%d path=/api/v1/%s/%d\n" % (
        d, s // 3600, (s // 60) % 60, s % 60, ms, rng.choice(LEVELS), rng.below(16),
        rng.choice(CITIES), rng.choice(WORDS), rng.below(10**7), rng.choice(STATUS),
        rng.below(65536), rng.choice(PATHS), rng.below(100000))


# Patterns whose hit counts are computed while generating (line based).
# kind: fixed = plain substring, regex = Python re (only syntax uvf also accepts).
SEARCH_PATTERNS = [
    ("nohit", "fixed", "UWTEST-NOHIT", []),
    ("needle", "fixed", "UWTEST-NEEDLE", []),
    ("rare", "fixed", "status=503", []),
    ("tokyo", "fixed", "東京", []),
    ("info", "fixed", "INFO", []),
    ("re5xx", "regex", r"status=5\d\d", ["-E"]),
    ("reid", "regex", r"id=12345\d\b", ["-E"]),
    ("ierror", "fixedi", "error", ["-i"]),
]


def count_patterns(data, patterns):
    text = data.decode("utf-8")
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    out = {}
    for name, kind, pat, _ in patterns:
        if kind == "fixed":
            out[name] = sum(1 for ln in lines if pat in ln)
        elif kind == "fixedi":
            p = pat.lower()
            out[name] = sum(1 for ln in lines if p in ln.lower())
        else:
            r = re.compile(pat)
            out[name] = sum(1 for ln in lines if r.search(ln))
    return out, len(lines)


def build_chunk(rng, chunk, line_fn, special=None):
    """Exactly `chunk` bytes of whole lines.  special=(index, line) puts one
    known line at that position.  The last line is padding ('#---')."""
    lines = []
    size = 0
    margin = 400
    while True:
        if special is not None and len(lines) == special[0]:
            ln = special[1].encode("utf-8")
        else:
            ln = line_fn(rng).encode("utf-8")
        if size + len(ln) > chunk - margin:
            break
        lines.append(ln)
        size += len(ln)
    rest = chunk - size
    lines.append(b"#" + b"-" * (rest - 2) + b"\n")
    data = b"".join(lines)
    assert len(data) == chunk
    return data


class ChunkFile:
    """A big file made of 1 MiB chunks picked from a pool by the PRNG.
    Hit counts and line numbers are known without reading the file back."""

    def __init__(self, name, line_fn, patterns, pool=128, chunk=MiB):
        self.patterns = patterns
        self.chunk = chunk
        rng = Rng(seed_of("pool", name))
        self.pool = []
        for _ in range(pool):
            data = build_chunk(rng, chunk, line_fn)
            cnt, nl = count_patterns(data, patterns)
            self.pool.append((data, cnt, nl))
        self.line_fn = line_fn
        self.name = name

    def write(self, w, total, needles=True):
        n = max(1, total // self.chunk)
        rng = Rng(seed_of("seq", self.name, total))
        positions = sorted(set([0, n // 2, n - 1])) if needles else []
        totals = {p[0]: 0 for p in self.patterns}
        lines_before = 0
        needle_lines = []
        for i in range(n):
            k = rng.below(len(self.pool))
            if i in positions:
                nrng = Rng(seed_of("needle", self.name, total, i))
                at = 100 + nrng.below(400)
                text = "2026-10-24T00:00:00.000Z INFO  [w00] 東京 UWTEST-NEEDLE chunk=%d\n" % i
                data = build_chunk(nrng, self.chunk, self.line_fn, special=(at, text))
                cnt, nl = count_patterns(data, self.patterns)
                needle_lines.append(lines_before + at + 1)
            else:
                data, cnt, nl = self.pool[k]
            w.write(data)
            for key, v in cnt.items():
                totals[key] += v
            lines_before += nl
        return {"bytes": n * self.chunk, "lines": lines_before, "hits": totals,
                "needle_lines": needle_lines}


# ====================================================================== sets
SETS = {}


def register(name, episodes, presets, doc):
    def deco(fn):
        SETS[name] = {"fn": fn, "episodes": episodes, "presets": presets, "doc": doc}
        return fn
    return deco


# ---------------------------------------------------------------- 1 huge / 3 cold-hot
@register("huge", [1, 3], {"smoke": ["16M", "64M"], "trial": ["1G", "10G"], "full": ["1G", "10G", "100G"]},
          "Log-like UTF-8 text of 1 GiB / 10 GiB / 100 GiB. Episodes 1 (size ladder) and 3 (cold vs hot).")
def gen_huge(sw, sizes):
    cf = ChunkFile("huge", log_line, [p for p in SEARCH_PATTERNS if p[0] in ("nohit", "needle")])
    meta = {}
    for s in sizes:
        total = parse_size(s)
        rel = "huge-%s.log" % size_label(total)
        w = sw.open(rel)
        info = cf.write(w, total)
        sw.add_file(rel, w)
        meta[rel] = info
        big = info["bytes"] >= 10 * GiB
        sw.case(id="huge-%s-nohit" % size_label(total), episode=1, files=rel, pattern="UWTEST-NOHIT",
                expect={"exit": 1, "hits": 0}, bytes=info["bytes"], timed=True,
                note="full scan, no output", big=big)
        sw.case(id="huge-%s-needle" % size_label(total), episode=1, files=rel, pattern="UWTEST-NEEDLE",
                expect={"exit": 0, "hits": len(info["needle_lines"]), "lines": info["needle_lines"]},
                bytes=info["bytes"], timed=True, note="3 hits: first, middle, last 1 MiB", big=big)
    return meta


# ---------------------------------------------------------------- 2 lines
@register("lines", [2], {"smoke": ["1M"], "trial": ["100M"], "full": ["100M", "1G"]},
          "Short fixed-width lines (8 bytes): 100 million and 1 billion lines.")
def gen_lines(sw, counts):
    L = MiB // 8  # lines per chunk
    alphabet = "0123456789abcdefghijklmnopqrstuvwxyz"
    rng = Rng(seed_of("lines-pool"))
    table = bytes(ord(alphabet[i % 36]) for i in range(256))
    pool = []
    for _ in range(32):
        rnd = b"".join(rng.u64().to_bytes(8, "little") for _ in range(L * 7 // 8 + 1)).translate(table)
        pool.append(b"".join(rnd[i * 7:i * 7 + 7] + b"\n" for i in range(L)))
    meta = {}
    for c in counts:
        n = parse_count(c)
        rel = "lines-%s.txt" % count_label(n)
        mid = n // 2 + 1
        w = sw.open(rel)
        seq = Rng(seed_of("lines-seq", n))
        written = 0
        while written < n:
            take = min(L, n - written)
            data = bytearray(pool[seq.below(len(pool))][: take * 8])
            if written < mid <= written + take:
                o = (mid - written - 1) * 8
                data[o:o + 8] = b"UWMID01\n"
            if written + take == n:
                data[-8:] = b"UWEND99\n"
            w.write(bytes(data))
            written += take
        sw.add_file(rel, w)
        meta[rel] = {"lines": n, "bytes": n * 8}
        big = n * 8 >= 10 * GiB
        for cid, pat, exp in (("end", "UWEND99", {"exit": 0, "hits": 1, "lines": [n]}),
                              ("mid", "UWMID01", {"exit": 0, "hits": 1, "lines": [mid]}),
                              ("nohit", "UWNONE0", {"exit": 1, "hits": 0})):
            sw.case(id="lines-%s-%s" % (count_label(n), cid), episode=2, files=rel, pattern=pat,
                    expect=exp, bytes=n * 8, timed=True, big=big,
                    note="line number of the last line = %d" % n if cid == "end" else "")
    return meta


# ---------------------------------------------------------------- 4 long lines
@register("longline", [4], {"smoke": ["1M", "8M"], "trial": ["10M", "100M"], "full": ["1M", "10M", "100M", "1G"]},
          "One line without any newline (minified JSON), and a long line between normal lines.")
def gen_longline(sw, sizes):
    rng = Rng(seed_of("longline-pool"))
    pool = []
    for _ in range(32):
        parts = []
        size = 0
        while size < MiB:
            it = ',{"id":%d,"city":"%s","v":%d}' % (rng.below(10**7), rng.choice(CITIES), rng.below(1000))
            parts.append(it)
            size += len(it.encode())
        pool.append("".join(parts).encode())
    meta = {}
    for s in sizes:
        total = parse_size(s)
        seq = Rng(seed_of("longline-seq", total))
        # 1) the whole file is one line
        rel = "oneline-%s.json" % size_label(total)
        w = sw.open(rel)
        w.write(b'{"data":[{"id":0,"city":"start"}')
        size = 0
        while size + MiB <= total:
            w.write(pool[seq.below(len(pool))])
            size += MiB
        w.write(',{"id":-1,"city":"UWTEST-NEEDLE"}]}'.encode())
        sw.add_file(rel, w)
        b = w.size
        sw.case(id="oneline-%s-needle" % size_label(total), episode=4, files=rel, pattern="UWTEST-NEEDLE",
                expect={"exit": 0, "hits": 1, "lines": [1]}, bytes=b, timed=True, quiet=True,
                note="needle at the very end of a single line")
        sw.case(id="oneline-%s-nohit" % size_label(total), episode=4, files=rel, pattern="UWTEST-NOHIT",
                expect={"exit": 1, "hits": 0}, bytes=b, timed=True)
        # 2) a long line between 1000 normal lines; needle after it
        rel2 = "longmix-%s.log" % size_label(total)
        w = sw.open(rel2)
        lrng = Rng(seed_of("longmix", total))
        for i in range(1, 1001):
            if i == 500:
                w.write(b'{"data":[')
                size = 0
                while size + MiB <= total:
                    w.write(pool[seq.below(len(pool))])
                    size += MiB
                w.write(b"]}\n")
            elif i == 900:
                w.write("2026-10-24T00:00:00.000Z INFO  [w00] UWTEST-NEEDLE after the long line\n".encode())
            else:
                w.write(log_line(lrng).replace("東京", "Tokyo").encode())
        sw.add_file(rel2, w)
        sw.case(id="longmix-%s-needle" % size_label(total), episode=4, files=rel2, pattern="UWTEST-NEEDLE",
                expect={"exit": 0, "hits": 1, "lines": [900]}, bytes=w.size, timed=True,
                note="line numbers after a %s line" % size_label(total))
        meta[rel] = {"bytes": b}
    return meta


# ---------------------------------------------------------------- 5 encodings
JA_LINES = [
    "東京駅の受付は九時に開きます。", "大阪の天気は晴れ、最高気温は二十度です。", "status=200 ok",
    "名古屋から新幹線で東京へ向かいます。", "ログの行番号を確かめる。", "札幌・仙台・福岡の支店",
    "半角ｶﾀｶﾅと全角カタカナ", "記号：「」『』【】※　〒", "数字 0123456789 と ＡＢＣ",
    "改行コードと文字コードの試験", "東京都千代田区丸の内一丁目", "漢字・ひらがな・カタカナの混在",
]


def ja_text(n=240):
    lines = []
    for i in range(n):
        lines.append("%04d %s" % (i + 1, JA_LINES[i % len(JA_LINES)]))
    return lines


def hits_of(lines, needle):
    return [i + 1 for i, ln in enumerate(lines) if needle in ln]


@register("encoding", [5], {"smoke": [""], "trial": [""], "full": [""]},
          "The same Japanese text in 11 encodings / BOM variants, plus broken bytes, short and Latin-1 files.")
def gen_encoding(sw, _):
    base = ja_text()
    text = "\n".join(base) + "\n"
    tokyo = hits_of(base, "東京")
    status = hits_of(base, "status=200")
    no_kana = [ln.replace("ｶﾀｶﾅ", "カタカナ") for ln in base]  # ISO-2022-JP has no half-width kana
    variants = [
        ("utf8.txt", text.encode("utf-8"), True, ""),
        ("utf8-bom.txt", b"\xef\xbb\xbf" + text.encode("utf-8"), True, ""),
        ("utf16le-bom.txt", b"\xff\xfe" + text.encode("utf-16-le"), True, ""),
        ("utf16be-bom.txt", b"\xfe\xff" + text.encode("utf-16-be"), True, ""),
        ("utf16le-nobom.txt", text.encode("utf-16-le"), False, "manual 6.1: UTF-16 without BOM is not detected"),
        ("utf16be-nobom.txt", text.encode("utf-16-be"), False, "manual 6.1: UTF-16 without BOM is not detected"),
        ("utf32le-bom.txt", b"\xff\xfe\x00\x00" + text.encode("utf-32-le"), None, "UTF-32 is not supported: error and exit code 2"),
        ("utf32be-bom.txt", b"\x00\x00\xfe\xff" + text.encode("utf-32-be"), None, "UTF-32 is not supported: error and exit code 2"),
        ("cp932.txt", text.encode("cp932"), True, ""),
        ("eucjp.txt", text.encode("euc_jp"), True, ""),
        ("iso2022jp.txt", ("\n".join(no_kana) + "\n").encode("iso2022_jp"), False,
         "manual 6.1: ISO-2022-JP is not detected"),
    ]
    for rel, data, spec, note in variants:
        sw.write_bytes(rel, data)
        if spec is None:  # refused on purpose
            for q in ("tokyo", "ascii"):
                sw.case(id="enc-%s-%s" % (rel[:-4], q), episode=5, files=rel,
                        pattern="東京" if q == "tokyo" else "status=200", expect={"exit": 2, "hits": 0}, note=note)
            continue
        sw.case(id="enc-%s-tokyo" % rel[:-4], episode=5, files=rel, pattern="東京",
                expect={"exit": 0, "hits": len(tokyo), "lines": tokyo}, documented_limit=not spec, note=note)
        sw.case(id="enc-%s-ascii" % rel[:-4], episode=5, files=rel, pattern="status=200",
                expect={"exit": 0, "hits": len(status), "lines": status}, documented_limit=not spec, note=note)
    # broken bytes: invalid UTF-8 sequences on lines without the search word
    rng = Rng(seed_of("enc-bad"))
    out = []
    for i, ln in enumerate(base):
        b = ln.encode("utf-8")
        if "東京" not in ln and i % 9 == 4:
            p = rng.below(len(b))
            b = b[:p] + b"\xff\xfe\xc0" + b[p:]
        out.append(b)
    sw.write_bytes("utf8-broken.txt", b"\n".join(out) + b"\n")
    sw.case(id="enc-utf8-broken-tokyo", episode=5, files="utf8-broken.txt", pattern="東京",
            expect={"exit": 0, "hits": len(tokyo), "lines": tokyo},
            note="invalid UTF-8 bytes on other lines must not hide the hits")
    # short files: almost nothing to guess from
    for rel, data in (("cp932-short.txt", "東京\n".encode("cp932")), ("eucjp-short.txt", "東京\n".encode("euc_jp"))):
        sw.write_bytes(rel, data)
        sw.case(id="enc-%s" % rel[:-4], episode=5, files=rel, pattern="東京",
                expect={"exit": 0, "hits": 1, "lines": [1]}, note="4-byte file: detection has little to go on")
    # half-width katakana only (valid as both Shift_JIS and EUC-JP lead bytes)
    hk = ["ｱｲｳｴｵ ｶｷｸｹｺ ｻｼｽｾｿ %d" % i for i in range(40)] + ["東京 ﾄｳｷｮｳ"]
    sw.write_bytes("cp932-hankaku.txt", ("\n".join(hk) + "\n").encode("cp932"))
    sw.case(id="enc-cp932-hankaku", episode=5, files="cp932-hankaku.txt", pattern="東京",
            expect={"exit": 0, "hits": 1, "lines": [41]}, note="mostly half-width katakana")
    # Latin-1 and pure ASCII
    lat = ["caf\u00e9 r\u00e9sum\u00e9 na\u00efve %d" % i for i in range(30)] + ["status=200 ok"]
    sw.write_bytes("latin1.txt", ("\n".join(lat) + "\n").encode("latin-1"))
    sw.case(id="enc-latin1-ascii", episode=5, files="latin1.txt", pattern="status=200",
            expect={"exit": 0, "hits": 1, "lines": [31]}, documented_limit=True,
            note="manual 6.1: Latin-1 is not detected; ASCII words should still be found")
    return {}


# ---------------------------------------------------------------- 6 newlines
@register("newline", [6], {"smoke": [""], "trial": [""], "full": [""]},
          "LF / CRLF / CR / mixed newlines, missing final newline, BOM, blank lines.")
def gen_newline(sw, _):
    body = ["line %02d" % i for i in range(1, 13)]
    body[3] = "line 04 UWTEST-NEEDLE"
    body[11] = "line 12 UWTEST-NEEDLE last"
    need = [4, 12]

    def join(seps, final=True):
        s = ""
        for i, ln in enumerate(body):
            s += ln
            if i < len(body) - 1 or final:
                s += seps[i % len(seps)]
        return s
    variants = [
        ("lf.txt", join(["\n"]).encode(), ""),
        ("crlf.txt", join(["\r\n"]).encode(), ""),
        ("cr.txt", join(["\r"]).encode(), "classic Mac OS newlines"),
        ("mixed.txt", join(["\n", "\r\n", "\r"]).encode(), "LF, CRLF and CR in one file"),
        ("lf-nofinal.txt", join(["\n"], final=False).encode(), "no newline at the end"),
        ("crlf-nofinal.txt", join(["\r\n"], final=False).encode(), "no newline at the end"),
        ("bom-crlf.txt", b"\xef\xbb\xbf" + join(["\r\n"]).encode(), "UTF-8 BOM + CRLF"),
        ("utf16le-crlf.txt", b"\xff\xfe" + join(["\r\n"]).encode("utf-16-le"), "UTF-16LE BOM + CRLF"),
        ("cp932-crlf.txt", join(["\r\n"]).replace("line", "行").encode("cp932"), "Shift_JIS + CRLF"),
        ("lfcr.txt", join(["\n\r"]).encode(), "LF CR pairs (not a standard newline)"),
    ]
    for rel, data, note in variants:
        sw.write_bytes(rel, data)
        exp = {"exit": 0, "hits": 2, "lines": need}
        if rel == "lfcr.txt":
            exp = {"exit": 0, "hits": 2}  # line numbers depend on the reading; observe
        sw.case(id="nl-%s" % rel[:-4], episode=6, files=rel, pattern="UWTEST-NEEDLE", expect=exp, note=note)
    # blank lines and a file of only newlines
    blank = "\n".join(["", "a", "", "", "UWTEST-NEEDLE", "", ""]) + "\n"
    sw.write_bytes("blank.txt", blank.encode())
    sw.case(id="nl-blank", episode=6, files="blank.txt", pattern="UWTEST-NEEDLE",
            expect={"exit": 0, "hits": 1, "lines": [5]}, note="blank lines count as lines")
    sw.write_bytes("only-newlines.txt", b"\n" * 1000)
    sw.case(id="nl-only-newlines", episode=6, files="only-newlines.txt", pattern="x",
            expect={"exit": 1, "hits": 0}, note="1000 empty lines")
    return {}


# ---------------------------------------------------------------- 7 CP932 duplicates
def cp932_duplicates():
    from collections import OrderedDict
    m = OrderedDict()
    for hi in list(range(0x81, 0xA0)) + list(range(0xE0, 0xFD)):
        for lo in list(range(0x40, 0x7F)) + list(range(0x80, 0xFD)):
            b = bytes([hi, lo])
            try:
                c = b.decode("cp932")
            except UnicodeDecodeError:
                continue
            m.setdefault(c, []).append(b)
    return OrderedDict((c, v) for c, v in m.items() if len(v) > 1)


def code_kind(b):
    hi = b[0]
    if hi == 0x87:
        return "NEC-special"
    if 0xED <= hi <= 0xEE:
        return "NEC-selected-IBM"
    if 0xFA <= hi <= 0xFC:
        return "IBM-ext"
    return "JIS"


@register("ja-dup", [7], {"smoke": [""], "trial": [""], "full": [""]},
          "CP932 characters that have two or three byte codes (NEC special, NEC-selected IBM, IBM extension).")
def gen_ja_dup(sw, _):
    dup = cp932_duplicates()
    rows = []
    for c, codes in dup.items():
        for b in codes:
            rows.append((c, b))
    out = bytearray()
    lineno = {}
    for i, (c, b) in enumerate(rows):
        out += b"%04d char=" % (i + 1) + b + (" code=%s kind=%s\r\n" % (b.hex().upper(), code_kind(b))).encode()
        lineno.setdefault(c, []).append(i + 1)
    sw.write_bytes("cp932-dup.txt", bytes(out))
    utf8 = ("".join("%04d char=%s code=%s kind=%s\r\n" % (i + 1, c, b.hex().upper(), code_kind(b))
                    for i, (c, b) in enumerate(rows))).encode("utf-8")
    sw.write_bytes("cp932-dup.utf8.txt", utf8)
    table = "char\tunicode\tcodes\n" + "".join(
        "%s\tU+%04X\t%s\n" % (c, ord(c), " ".join("%s(%s)" % (b.hex().upper(), code_kind(b)) for b in v))
        for c, v in dup.items())
    sw.write_bytes("cp932-dup-table.tsv", table.encode("utf-8"))
    picks = ["∵", "≒", "￢", "Ⅰ", "ⅰ", "髙", "﨑", "∪", "№"]
    for c in picks:
        if c not in dup:
            continue
        ln = lineno[c]
        sw.case(id="dup-U%04X" % ord(c), episode=7, files="cp932-dup.txt", pattern=c,
                expect={"exit": 0, "hits": len(ln), "lines": ln},
                note="%s has %d codes: %s" % (c, len(ln), " ".join(b.hex().upper() for b in dup[c])))
        sw.case(id="dup-U%04X-utf8" % ord(c), episode=7, files="cp932-dup.utf8.txt", pattern=c,
                expect={"exit": 0, "hits": len(ln), "lines": ln}, note="UTF-8 control")
    # one-code characters as baseline
    base = "①\r\n②\r\n東京\r\n".encode("cp932")
    sw.write_bytes("cp932-single.txt", base)
    sw.case(id="dup-single", episode=7, files="cp932-single.txt", pattern="①",
            expect={"exit": 0, "hits": 1, "lines": [1]}, note="NEC special with a single code")
    return {"duplicates": len(dup), "rows": len(rows)}


# ---------------------------------------------------------------- 8 dame-moji (0x5C etc.)
DAME_WORDS = ["表示", "ソフト", "能力", "十分", "予定", "申請", "構造", "ポイント", "暴走", "本当", "噂話", "欺く"]


@register("ja-dame", [8], {"smoke": [""], "trial": [""], "full": [""]},
          "Shift_JIS characters whose 2nd byte is \\ | [ ] { } ` (dame-moji), against real ASCII symbols.")
def gen_ja_dame(sw, _):
    lines = []
    for i, w in enumerate(DAME_WORDS):
        lines.append("%02d %sを確かめる" % (i + 1, w))
    lines += ["path C:\\Users\\test", "pipe a|b|c", "brackets [x] {y} `z`", "東京の表と裏", "ソースの表記",
              "plain ascii line"]
    text = "\n".join(lines) + "\n"
    sw.write_bytes("cp932-dame.txt", text.encode("cp932"))
    sw.write_bytes("utf8-dame.txt", text.encode("utf-8"))
    # dame list for the article
    dame = []
    for c in sorted(set("".join(DAME_WORDS) + "表ソ能十予申構ポ暴本噂欺")):
        b = c.encode("cp932")
        if len(b) == 2 and b[1] in (0x5C, 0x7C, 0x5B, 0x5D, 0x7B, 0x7D, 0x60):
            dame.append("%s\t%s\t2nd byte %s\n" % (c, b.hex().upper(), repr(chr(b[1]))))
    sw.write_bytes("dame-list.tsv", ("char\tcp932\tnote\n" + "".join(dame)).encode("utf-8"))
    queries = [
        ("hyo", "表", [], "fixed"), ("so", "ソ", [], "fixed"), ("noryoku", "能力", [], "fixed"),
        ("backslash", "\\", [], "fixed"), ("pipe", "|", [], "fixed"), ("bracket", "[", [], "fixed"),
        ("re-alt", "表|ソ", ["-E"], r"表|ソ"), ("re-class", "[表ソ能]", ["-E"], r"[表ソ能]"),
        ("re-dot", "能.", ["-E"], r"能."), ("re-backslash", r"\\", ["-E"], r"\\"),
        ("re-pipe", r"a\|b", ["-E"], r"a\|b"),
    ]
    for qid, pat, flags, ref in queries:
        if ref == "fixed":
            hit = [i + 1 for i, ln in enumerate(lines) if pat in ln]
        else:
            r = re.compile(ref)
            hit = [i + 1 for i, ln in enumerate(lines) if r.search(ln)]
        for rel in ("cp932-dame.txt", "utf8-dame.txt"):
            sw.case(id="dame-%s-%s" % (qid, rel.split("-")[0]), episode=8, files=rel, pattern=pat, flags=flags,
                    expect={"exit": 0 if hit else 1, "hits": len(hit), "lines": hit},
                    note="Shift_JIS 2nd byte collides with ASCII" if rel.startswith("cp932") else "UTF-8 control")
    return {}


# ---------------------------------------------------------------- 9 wave dash / width / NFC-NFD
@register("ja-wave", [9], {"smoke": [""], "trial": [""], "full": [""]},
          "Wave dash and minus look-alikes, full/half width, NFC/NFD text and file names (names made at run time).")
def gen_ja_wave(sw, _):
    pairs = [("～", "FULLWIDTH TILDE U+FF5E (Windows CP932 0x8160)"), ("〜", "WAVE DASH U+301C (JIS / Mac)"),
             ("－", "FULLWIDTH HYPHEN-MINUS U+FF0D (CP932 0x817C)"), ("−", "MINUS SIGN U+2212 (JIS / Mac)"),
             ("∥", "PARALLEL TO U+2225 (CP932 0x8161)"), ("‖", "DOUBLE VERTICAL LINE U+2016 (JIS / Mac)")]
    lines = ["%02d 9時%s17時 %s" % (i + 1, c, d) for i, (c, d) in enumerate(pairs)]
    lines += ["07 ABC half", "08 ＡＢＣ full", "09 ｶﾀｶﾅ half", "10 カタカナ full", "11 全角　スペース", "12 半角 スペース"]
    utf8 = ("\n".join(lines) + "\n").encode("utf-8")
    sw.write_bytes("utf8-wave.txt", utf8)
    # CP932 file as Windows writes it: U+FF5E etc. -> 0x8160 ...
    cp = []
    for ln in lines:
        try:
            cp.append(ln.encode("cp932"))
        except UnicodeEncodeError:
            cp.append(ln.encode("shift_jis"))
    sw.write_bytes("cp932-wave.txt", b"\n".join(cp) + b"\n")
    for q, name in (("～", "tilde-ff5e"), ("〜", "wave-301c"), ("－", "minus-ff0d"), ("−", "minus-2212"),
                    ("ABC", "abc"), ("ＡＢＣ", "abc-full"), ("カタカナ", "kata"), ("ｶﾀｶﾅ", "kata-half"), ("　", "ideo-space")):
        exact = [i + 1 for i, ln in enumerate(lines) if q in ln]
        sw.case(id="wave-%s-utf8" % name, episode=9, files="utf8-wave.txt", pattern=q,
                expect={"exit": 0, "hits": len(exact), "lines": exact}, note="exact code point match in UTF-8")
        sw.case(id="wave-%s-cp932" % name, episode=9, files="cp932-wave.txt", pattern=q, expect={},
                observe=True, note="observe: which Unicode does 0x8160 etc. become")
    # NFC / NFD contents
    word = "がぎぐげご パピプペポ"
    nfc = ("01 " + unicodedata.normalize("NFC", word) + "\n").encode("utf-8")
    nfd = ("01 " + unicodedata.normalize("NFD", word) + "\n").encode("utf-8")
    sw.write_bytes("nfc-text.txt", nfc)
    sw.write_bytes("nfd-text.txt", nfd)
    sw.case(id="wave-nfc-text", episode=9, files="nfc-text.txt", pattern="が", expect={"exit": 0, "hits": 1, "lines": [1]})
    sw.case(id="wave-nfd-text", episode=9, files="nfd-text.txt", pattern="が", expect={}, observe=True,
            note="observe: NFC query against NFD text (か + U+3099)")
    # NFC / NFD file names: created at run time (never committed to git)
    sw.case(id="names-nfc-glob", episode=9, kind="names", files="names/nfc/*が*.txt", pattern="UWTEST-NAME",
            expect={"exit": 0, "hits": 1}, note="file name stored as NFC")
    sw.case(id="names-nfd-glob", episode=9, kind="names", files="names/nfd/*が*.txt", pattern="UWTEST-NAME",
            expect={}, observe=True, note="file name stored as NFD (as macOS Finder/Mac zip may produce)")
    sw.case(id="names-all", episode=9, kind="names", files="names/**/*.txt", pattern="UWTEST-NAME",
            expect={"exit": 0, "hits": 2}, note="both names found by a plain wildcard")
    return {}


def make_names(root):
    """Called by run.py: write the NFC / NFD named files under <set>/names/."""
    for form in ("nfc", "nfd"):
        d = os.path.join(root, "names", form)
        os.makedirs(d, exist_ok=True)
        name = unicodedata.normalize(form.upper(), "がぎぐ.txt")
        with open(os.path.join(d, name), "wb") as f:
            f.write(b"UWTEST-NAME\n")


# ---------------------------------------------------------------- 10 rare characters
@register("ja-rare", [10], {"smoke": [""], "trial": [""], "full": [""]},
          "Surrogate pairs, ideographic variation sequences, JIS level 3/4, user-defined characters, old encodings.")
def gen_ja_rare(sw, _):
    lines = ["01 𠮷野家", "02 吉野家", "03 葛\U000E0100飾区", "04 葛飾区", "05 𩸽の塩焼き", "06 丂と鷗外",
             "07 私用領域 \ue000 \ue001", "08 𠀋𡈽𡌛 第4水準", "09 👍 絵文字", "10 é と e\u0301"]
    text = "\n".join(lines) + "\n"
    sw.write_bytes("utf8-rare.txt", text.encode("utf-8"))
    sw.write_bytes("utf16le-rare.txt", b"\xff\xfe" + text.encode("utf-16-le"))
    for q, qid in (("𠮷", "tsuchiyoshi"), ("葛", "katsura"), ("葛\U000E0100", "katsura-ivs"), ("𩸽", "hokke"),
                   ("👍", "emoji"), ("\ue000", "pua")):
        exact = [i + 1 for i, ln in enumerate(lines) if q in ln]
        for rel in ("utf8-rare.txt", "utf16le-rare.txt"):
            sw.case(id="rare-%s-%s" % (qid, rel.split("-")[0]), episode=10, files=rel, pattern=q,
                    expect={"exit": 0, "hits": len(exact), "lines": exact}, note="")
    sw.case(id="rare-onechar-utf8", episode=10, files="utf8-rare.txt", pattern=r"^01 .野家$", flags=["-E"],
            expect={"exit": 0, "hits": 1, "lines": [1]}, note="𠮷 (a surrogate pair in UTF-16) is one character for '.'")
    sw.case(id="rare-onechar-utf16", episode=10, files="utf16le-rare.txt", pattern=r"^01 .野家$", flags=["-E"],
            expect={"exit": 0, "hits": 1, "lines": [1]}, note="same in UTF-16")
    # CP932 user-defined area (gaiji) F040-F9FC -> Unicode PUA U+E000..
    g = b"01 gaiji \xf0\x40 \xf0\x41\r\n02 \x93\x8c\x8b\x9e\r\n"
    sw.write_bytes("cp932-gaiji.txt", g)
    sw.case(id="rare-gaiji-pua", episode=10, files="cp932-gaiji.txt", pattern="\ue000",
            expect={"exit": 0, "hits": 1, "lines": [1]}, note="CP932 F040 = U+E000 (Microsoft table)")
    sw.case(id="rare-gaiji-tokyo", episode=10, files="cp932-gaiji.txt", pattern="東京",
            expect={"exit": 0, "hits": 1, "lines": [2]}, note="text after gaiji is still found")
    # EUC-JP with JIS X 0212 (3-byte, 0x8F ...)
    e = "01 丂 補助漢字\n02 東京\n03 鷗外\n".encode("euc_jp")
    sw.write_bytes("eucjp-0212.txt", e)
    for q, ln in (("丂", [1]), ("東京", [2]), ("鷗", [3])):
        sw.case(id="rare-euc0212-U%04X" % ord(q[0]), episode=10, files="eucjp-0212.txt", pattern=q,
                expect={"exit": 0, "hits": 1, "lines": ln}, note="EUC-JP 3-byte JIS X 0212" if q != "東京" else "")
    # ISO-2022-JP (JIS code)
    j = "01 東京\n02 大阪\n".encode("iso2022_jp")
    sw.write_bytes("iso2022jp-short.txt", j)
    sw.case(id="rare-iso2022jp", episode=10, files="iso2022jp-short.txt", pattern="東京",
            expect={"exit": 0, "hits": 1, "lines": [1]}, documented_limit=True,
            note="manual 6.1: ISO-2022-JP is not detected")
    return {}


# ---------------------------------------------------------------- 11 compressed
def _tool(name):
    return shutil.which(name)


@register("compress", [11], {"smoke": ["16M"], "trial": ["256M"], "full": ["1G"]},
          "The same log text as plain, gz, bz2, xz, lzma, zst, lz4, br (+ zip / tar.gz that uvf refuses).")
def gen_compress(sw, sizes):
    cf = ChunkFile("compress", log_line, [p for p in SEARCH_PATTERNS if p[0] in ("needle", "rare")])
    meta = {}
    for s in sizes:
        total = parse_size(s)
        lab = size_label(total)
        rel = "c-%s.log" % lab
        w = sw.open(rel)
        info = cf.write(w, total)
        sw.add_file(rel, w)
        meta[rel] = info
        if sw.hash_only:
            continue
        src = sw.path(rel)
        made = [("plain", rel)]
        t0 = time.time()
        with open(src, "rb") as f, open(src + ".gz", "wb") as raw:
            with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=6) as g:
                shutil.copyfileobj(f, g, 8 * MiB)
        made.append(("gz", rel + ".gz"))
        with open(src, "rb") as f, bz2.open(src + ".bz2", "wb", compresslevel=9) as g:
            shutil.copyfileobj(f, g, 8 * MiB)
        made.append(("bz2", rel + ".bz2"))
        with open(src, "rb") as f, lzma.open(src + ".xz", "wb", preset=6) as g:
            shutil.copyfileobj(f, g, 8 * MiB)
        made.append(("xz", rel + ".xz"))
        with open(src, "rb") as f, lzma.open(src + ".lzma", "wb", format=lzma.FORMAT_ALONE, preset=6) as g:
            shutil.copyfileobj(f, g, 8 * MiB)
        made.append(("lzma", rel + ".lzma"))
        for ext, tool, args in (("zst", "zstd", ["-q", "-3", "-f", "-o"]), ("lz4", "lz4", ["-q", "-f"]),
                                ("br", "brotli", ["-q", "5", "-f", "-o"])):
            exe = _tool(tool)
            if not exe:
                eprint("  %s not found: .%s is skipped (install it to test this format)" % (tool, ext))
                sw.case(id="comp-%s-%s" % (lab, ext), episode=11, files=rel + "." + ext, pattern="UWTEST-NEEDLE",
                        expect={}, missing="%s not installed" % tool)
                continue
            if tool == "lz4":
                cmd = [exe] + args + [src, src + ".lz4"]
            else:
                cmd = [exe] + args + [src + "." + ext, src]
            subprocess.run(cmd, check=True)
            made.append((ext, rel + "." + ext))
        # archives that uvf refuses: a 1 MiB sample is enough
        with open(src, "rb") as f:
            sample = f.read(MiB)
        zi = zipfile.ZipInfo("c-sample.log", date_time=(2026, 10, 24, 0, 0, 0))
        with zipfile.ZipFile(sw.path("c-sample.zip"), "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr(zi, sample)
        ti = tarfile.TarInfo("c-sample.log")
        ti.size = len(sample)
        ti.mtime = 0
        with tarfile.open(sw.path("c-sample.tar.gz"), "w:gz") as t:
            t.addfile(ti, io.BytesIO(sample))
        eprint("  compressed %s in %.0f s" % (rel, time.time() - t0))
        for fmt, f in made:
            b = os.path.getsize(sw.path(f))
            sw.case(id="comp-%s-%s" % (lab, fmt), episode=11, files=f, pattern="UWTEST-NEEDLE",
                    expect={"exit": 0, "hits": len(info["needle_lines"]), "lines": info["needle_lines"]},
                    bytes=b, timed=True, raw_bytes=info["bytes"], note=fmt)
            sw.case(id="comp-%s-%s-rare" % (lab, fmt), episode=11, files=f, pattern="status=503",
                    expect={"exit": 0, "hits": info["hits"]["rare"]}, bytes=b, timed=True, quiet=True,
                    raw_bytes=info["bytes"], note=fmt + ", many hits")
        for fmt in ("zip", "tar.gz"):
            sw.case(id="comp-sample-%s" % fmt.replace(".", ""), episode=11, files="c-sample." + fmt,
                    pattern="UWTEST-NEEDLE", expect={"exit": 2},
                    note="manual 6: uvf refuses %s with a message (exit 2); the paid edition reads zip" % fmt)
    return meta


# ---------------------------------------------------------------- 12 many files
@register("many", [12], {"smoke": ["500"], "trial": ["10K"], "full": ["10K", "100K"]},
          "10,000 / 100,000 small files in a deep tree, with folders excluded by .ignore.")
def gen_many(sw, counts):
    meta = {}
    for c in counts:
        n = parse_count(c)
        lab = count_label(n)
        top = "many-%s" % lab
        rng = Rng(seed_of("many", n))
        needle_files = 0
        tokyo_lines = 0
        for i in range(n):
            depth = 1 + rng.below(8)
            parts = ["d%d" % rng.below(10) for _ in range(depth)]
            rel = "%s/%s/f%06d.txt" % (top, "/".join(parts), i)
            lines = [log_line(rng) for _ in range(5 + rng.below(40))]
            if i % 997 == 13:
                lines[rng.below(len(lines))] = "UWTEST-NEEDLE in file %d\n" % i
                needle_files += 1
            tokyo_lines += sum(1 for ln in lines if "東京" in ln)
            sw.write_bytes(rel, "".join(lines).encode("utf-8"))
        # excluded by .ignore (works with or without git): 50 files that all contain the needle
        sw.write_bytes("%s/.ignore" % top, b"ignored/\n*.tmp\n")
        for i in range(50):
            sw.write_bytes("%s/ignored/x%02d.txt" % (top, i), b"UWTEST-NEEDLE ignored\n")
        sw.write_bytes("%s/d0/skip.tmp" % top, b"UWTEST-NEEDLE tmp\n")
        meta[top] = {"files": n, "needle_files": needle_files}
        sw.case(id="many-%s-needle" % lab, episode=12, files="%s/**/*.txt" % top, pattern="UWTEST-NEEDLE",
                expect={"exit": 0, "hits": needle_files}, timed=True, note="files excluded by .ignore must be skipped")
        sw.case(id="many-%s-noignore" % lab, episode=12, files="%s/**/*.txt" % top, pattern="UWTEST-NEEDLE",
                flags=["--no-ignore"], expect={"exit": 0, "hits": needle_files + 50}, timed=True)
        sw.case(id="many-%s-tokyo" % lab, episode=12, files="%s/**/*.txt" % top, pattern="東京",
                expect={"exit": 0, "hits": tokyo_lines}, timed=True, quiet=True)
        sw.case(id="many-%s-files" % lab, episode=12, kind="files", files="%s/**/*.txt" % top, pattern="",
                flags=["--files"], expect={"exit": 0, "files": n}, note="--files lists what the wildcard expands to")
    return meta


# ---------------------------------------------------------------- 13 search
@register("search", [13], {"smoke": ["64M"], "trial": ["1G"], "full": ["1G", "10G"]},
          "One log file, searched for 0 / few / thousands / millions of hits, fixed, regex and -i.")
def gen_search(sw, sizes):
    cf = ChunkFile("search", log_line, SEARCH_PATTERNS)
    meta = {}
    for s in sizes:
        total = parse_size(s)
        lab = size_label(total)
        rel = "search-%s.log" % lab
        w = sw.open(rel)
        info = cf.write(w, total)
        sw.add_file(rel, w)
        meta[rel] = info
        for name, kind, pat, flags in SEARCH_PATTERNS:
            exp = {"exit": 0 if info["hits"][name] else 1, "hits": info["hits"][name]}
            if name == "needle":
                exp["lines"] = info["needle_lines"]
            sw.case(id="search-%s-%s" % (lab, name), episode=13, files=rel, pattern=pat, flags=flags,
                    expect=exp, bytes=info["bytes"], timed=True, quiet=info["hits"][name] > 1000,
                    big=total >= 10 * GiB, note="%s hits" % format(info["hits"][name], ","))
    return meta


# ---------------------------------------------------------------- 14 abnormal
@register("abnormal", [14], {"smoke": [""], "trial": [""], "full": [""]},
          "Empty file, binary bytes, a log that grows while searched, no permission, broken gzip, wrong extension.")
def gen_abnormal(sw, _):
    sw.write_bytes("empty.txt", b"")
    sw.case(id="ab-empty", episode=14, files="empty.txt", pattern="x", expect={"exit": 1, "hits": 0})
    rng = Rng(seed_of("abnormal-bin"))
    blob = bytearray()
    for i in range(2000):
        blob += bytes(rng.below(256) for _ in range(rng.below(64)))
        if i == 1500:
            blob += b"\nUWTEST-NEEDLE inside binary\n"
    sw.write_bytes("binary.bin", bytes(blob))
    sw.case(id="ab-binary", episode=14, files="binary.bin", pattern="UWTEST-NEEDLE", expect={"exit": 0, "hits": 1},
            note="NUL and random bytes around a text line")
    sw.write_bytes("nul-mixed.txt", b"line1\nline2 \x00\x00 UWTEST-NEEDLE\nline3\n")
    sw.case(id="ab-nul", episode=14, files="nul-mixed.txt", pattern="UWTEST-NEEDLE",
            expect={"exit": 0, "hits": 1, "lines": [2]})
    sw.write_bytes("growing.log", b"")
    sw.case(id="ab-growing", episode=14, kind="growing", files="growing.log", pattern="UWTEST-NEEDLE",
            expect={"exit_in": [0, 1]}, note="another process appends 200,000 lines while uvf reads")
    sw.write_bytes("noperm.txt", b"UWTEST-NEEDLE\n")
    sw.case(id="ab-noperm", episode=14, kind="noperm", files="noperm.txt", pattern="UWTEST-NEEDLE",
            expect={"exit": 2}, note="permission removed at run time (skipped on Windows and as root)")
    sw.case(id="ab-missing", episode=14, files="no-such-file.txt", pattern="x", expect={"exit": 2})
    os_dir = "a-folder"
    sw.write_bytes(os_dir + "/inside.txt", b"x\n")
    sw.case(id="ab-folder", episode=14, files=os_dir, pattern="x", expect={"exit_in": [0, 2]},
            note="a folder given where a file is expected")
    good = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=good, mtime=0) as g:
        g.write(b"".join(b"line %d UWTEST-NEEDLE\n" % i for i in range(5000)))
    raw = good.getvalue()
    sw.write_bytes("truncated.log.gz", raw[: len(raw) // 2])
    sw.case(id="ab-truncated-gz", episode=14, files="truncated.log.gz", pattern="UWTEST-NEEDLE",
            expect={"exit": 2}, note="cut in the middle: must report an error, not hang or crash")
    sw.write_bytes("not-gzip.log.gz", b"plain text UWTEST-NEEDLE\n")
    sw.case(id="ab-not-gzip", episode=14, files="not-gzip.log.gz", pattern="UWTEST-NEEDLE",
            expect={"exit": 2}, note="manual 6: .gz that is not gzip is reported")
    sw.write_bytes("bad-regex.txt", b"abc\n")
    sw.case(id="ab-bad-regex", episode=14, files="bad-regex.txt", pattern="(abc", flags=["-E"], expect={"exit": 2})
    sw.case(id="ab-symlink-loop", episode=14, kind="symlink-loop", files="loop/**/*.txt", pattern="UWTEST-NEEDLE",
            expect={"exit_in": [0, 1, 2]}, note="a folder link that points to itself (made at run time, not on Windows)")
    return {}


# ====================================================================== main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--work", help="folder for the generated data (outside the repository)")
    ap.add_argument("--preset", choices=["smoke", "trial", "full"], default="smoke")
    ap.add_argument("--set", action="append", help="only these sets (repeatable)")
    ap.add_argument("--sizes", help="override sizes / counts, comma separated (with one --set)")
    ap.add_argument("--hash-only", action="store_true", help="compute SHA-256 without writing files")
    ap.add_argument("--list", action="store_true")
    a = ap.parse_args()
    if a.list:
        for k, v in SETS.items():
            print("%-9s ep %-6s %s" % (k, ",".join(map(str, v["episodes"])), v["doc"]))
            print("%9s sizes: %s" % ("", "  ".join("%s=%s" % (p, ",".join(s) or "-") for p, s in v["presets"].items())))
        return 0
    if not a.work and not a.hash_only:
        ap.error("--work is required")
    names = a.set or list(SETS)
    sums = {}
    for name in names:
        s = SETS[name]
        sizes = a.sizes.split(",") if a.sizes else s["presets"][a.preset]
        t0 = time.time()
        eprint("[%s] %s" % (name, ",".join(x for x in sizes if x) or "fixed"))
        sw = SetWriter(a.work or ".", name, hash_only=a.hash_only)
        meta = s["fn"](sw, sizes)
        sums[name] = sw.finish({"preset": a.preset, "sizes": sizes, "info": meta})
        eprint("[%s] %d files, %d cases, %.1f s" % (name, len(sw.files), len(sw.cases), time.time() - t0))
    if a.hash_only:
        for name, text in sums.items():
            sys.stdout.write("# %s\n%s" % (name, text))
    return 0


if __name__ == "__main__":
    sys.exit(main())
