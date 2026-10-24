# Result files

`tools/run.py` writes one folder per run: `results/<date>_<machine>/`.

| file | content |
|---|---|
| `env.json` | machine, OS, CPU, memory, file system, virtualization, product versions, options; `rejudged` lists when and why the folder was judged again with `tools/rejudge.py` (the right answers changed; exit codes, hits and seconds are the measured ones) |
| `runs.csv` | one row per execution (written while running) |
| `summary.csv` | one row per product × case × cache: median, min, max |
| `failures.csv` | rows of `summary.csv` with a wrong answer, crash or timeout |

## runs.csv

| column | meaning |
|---|---|
| suite | version of this test suite |
| run_id, timestamp | when |
| machine, os, os_version, fs | where (see env.json) |
| product, product_version | `uvf` / `uvp` and its `--version` |
| set, case, episode | which data and which case (`cases.json`) |
| file_bytes | size of the searched file (compressed size for compressed files) |
| cache | `first` (first search of the file in the session), `cold`, `hot`, or `-` for cases that are only checked for the right answer |
| run | 1..N |
| seconds | wall-clock seconds, start to exit, output written to a file |
| exit | exit code (0 found, 1 not found, 2 error) |
| hits | result lines counted by the runner |
| hits_reported | the count the product printed itself (stderr), if any |
| expected_hits | the right answer |
| correct | `ok`, `ng` (wrong), `obs` (no single right answer; recorded), `crash`, `timeout`, `skip`, `missing` |
| peak_rss_mib | peak memory of the process |
| note | why it is wrong, or what the case is about (messages of the product are kept in `stdout/*.err`, not in the CSV) |

## summary.csv

Same keys, plus `runs`, `median_s`, `min_s`, `max_s`, `mb_per_s` (file_bytes / median).
`correct` is `ok` only when every run was `ok`.
