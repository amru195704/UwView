# UwView test series

Reproducible tests of **uvf** (the command-line search of the free UwView) and **uvp** (the paid
edition, UwView Pro) on macOS, Linux and Windows. This folder is the primary record behind the
"UwView test" blog series: the data generators, the procedure, the raw results and the weak points.

* Rules (cold / hot, medians, the 1.5× rule): [docs/00-rules.md](docs/00-rules.md)
* Episodes 1–15: [docs/](docs/)
* Machines: [docs/environment.md](docs/environment.md)
* Result files: [schema/results.md](schema/results.md)
* Weak points found so far: [weak-points.md](weak-points.md)

## Reproduce

Requirements: Python 3.9 or newer (standard library only), `uvf` and/or `uvp` on the PATH.
Optional: `zstd`, `lz4`, `brotli` commands for episode 11.

```sh
cd testsuite
python3 tools/gen.py --list                                   # the data sets
python3 tools/gen.py --work ~/uwtest --preset smoke           # small data, about a minute
python3 tools/check.py data --work ~/uwtest                   # SHA-256 against manifests/
python3 tools/run.py --work ~/uwtest --label my-machine       # hot runs, uvf
python3 tools/summarize.py ~/uwtest/results/*                 # Markdown tables
```

Presets: `smoke` (minutes, small files), `trial` (1 GiB and 10 GiB), `full` (up to 100 GiB and
1 billion lines; about 250 GB of free space). Keep the work folder **outside** the repository.
A single set or size: `--set huge --sizes 1G`.

Cold runs drop the OS file cache and need administrator rights:

```sh
python3 tools/run.py --work ~/uwtest --label mac-m4 --set huge --cache cold,hot --product uvf --product uvp
```

On Windows use `py tools\run.py ... --uvf C:\path\to\uvf.exe` and, for cold, `--rammap C:\path\to\RAMMap64.exe`
from an administrator prompt.

## Layout

| path | content |
|---|---|
| `tools/gen.py` | makes every data set from a fixed seed (same bytes on every OS) |
| `tools/run.py` | runs the cases, checks every answer, writes `runs.csv` / `summary.csv` / `env.json` |
| `tools/check.py` | compares generated data with `manifests/`, checks CSV columns |
| `tools/summarize.py` | tables per episode, list of wrong answers |
| `testdata/` | the small data sets as committed files (encodings, newlines, Japanese, broken files) |
| `manifests/` | SHA-256 of every generated file (large sets included) |
| `results/` | raw results per machine and date |
| `docs/` | one page per episode |

License: the scripts in `tools/` are MIT (see the SPDX line in each file). The generated data is
synthetic and free to use.
