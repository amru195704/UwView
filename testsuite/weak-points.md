# Weak points

Wrong answers found by this suite. They are **published as found and not fixed first**; each one is
a target for the next version, and a fix gets a follow-up post. "Documented" means the manual already
says it is not supported.

Found while building the suite (2026-10-24): Linux x86_64 (uvf 1.9.1, `smoke`) and macOS on Apple M4
(uvf 1.9.1 / uvp 1.9.1, `trial`: episodes 1, 5, 7). Linux in VMware and Windows 11 are still to come.

| # | episode | case | what happens | expected | found in | status |
|---|---|---|---|---|---|---|
| 1 | 5, 6, 10 | `enc-utf16le-bom-*`, `nl-utf16le-crlf`, `rare-*-utf16le` | UTF-16LE files with a BOM: only the first line is found; later lines give no hits | every line is searched (manual 6.1) | uvf 1.9.1 (macOS, Linux). uvp 1.9.1 is right | open |
| 2 | 5 | `enc-utf16be-bom-*` | UTF-16BE with BOM: hits are right, but each result line ends with a replacement character `�` | clean line text | uvf 1.9.1 (Linux) | open |
| 3 | 5 | `enc-utf8-broken-tokyo` | a few invalid bytes anywhere make the whole UTF-8 file be read as Shift_JIS, so no Japanese word is found | UTF-8 lines found; only the broken bytes are lost | uvf 1.9.1, uvp 1.9.1 | open (follows the detection rule of manual 6.1) |
| 4 | 6 | `nl-cr`, `nl-mixed` | a lone CR (classic Mac) is not a line break: line numbers shift | CR, LF and CRLF each end a line | uvf 1.9.1 (Linux) | open |
| 5 | 10 | `rare-onechar-*` | in a regular expression `.` matches half of 𠮷 (a surrogate pair); `..` is needed | `.` matches one character | uvf 1.9.1 (Linux) | open |
| 6 | 10 | `rare-euc0212-*` | EUC-JP 3-byte characters (JIS X 0212, e.g. 丂 鷗) are not found; other text in the file is | found | uvf 1.9.1 (Linux) | open |
| 7 | 5 | `enc-utf32*-bom-*` | UTF-32 is not supported. uvp stops with a message (exit 2); uvf 1.9.1 tried to read UTF-32LE and found only line 1 | message and exit code 2 | uvf 1.9.1 | fixed for the next version (uvf and the free GUI refuse UTF-32 like uvp) |
| – | 5, 10 | `enc-utf16*-nobom-*`, `enc-iso2022jp-*`, `rare-iso2022jp` | not detected | – | uvf / uvp 1.9.1 | documented (manual 6.1) |

Observations without a single right answer (episode 9): an NFC search word does not find NFD text
(か + U+3099); on Linux (ext4) an NFC wildcard does not match an NFD file name. Recorded as `obs`.
