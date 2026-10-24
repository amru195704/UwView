# 11 — Compressed files without unpacking

**Question.** How much slower is searching inside gz, bz2, xz, lzma, zst, lz4, br than the plain file?

## Data (`compress`)

* `c-<size>.log`: log text. It is compressed by `gen.py`: gz, bz2, xz, lzma with Python, zst / lz4 / br with the `zstd`, `lz4`, `brotli` commands if installed (otherwise those cases are `missing`).
* Compressed files are not in the manifest (compressors differ between versions); the plain file is.

| preset | sizes |
|---|---|
| smoke | 16M |
| trial | 256M |
| full | 1G |

## Run

```sh
python3 tools/gen.py   --work WORK --preset full --set compress
python3 tools/check.py data --work WORK
python3 tools/run.py   --work WORK --label MACHINE --set compress --cache cold,hot --product uvf --product uvp
```

## What is checked

* The needle (3 lines) and `status=503` (many lines) give the same answer in every format.
* `c-sample.zip` and `c-sample.tar.gz`: uvf refuses them with a message and exit code 2 (manual).

## By hand

GUI: open the .gz and .xz files and compare with the plain file.

## Notes

* MB/s in `summary.csv` is computed from the compressed size; the article also shows it per uncompressed MB.

## Results

`../results/<date>_<machine>/summary.csv` (episode = 11). Tables: `python3 tools/summarize.py ../results/*`.
