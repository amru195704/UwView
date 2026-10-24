# Machines

The machines are the same as in the earlier UwView comparison posts. `tools/run.py` also writes the
details it can read (`env.json`: OS version, CPU, cores, memory, file system, virtualization,
product versions) next to every result, so this page and the CSV never disagree.

| label | machine | memory | storage | file system | notes |
|---|---|---|---|---|---|
| `mac-m4-32g-usbssd` | MacBook Air, Apple M4 (10 cores) | 32 GB | external USB SSD | APFS | real machine |
| `linux-vmware-fusion` | VMware Fusion guest on the Mac above | (to be filled: vCPU / memory) | virtual disk on (to be filled) | ext4 | cold needs the host cache dropped too |
| `win11-16g` | Windows 11 notebook | 16 GB | internal SSD | NTFS | (to be filled) |

Versions: the product version is read from `uvf --version` / `uvp --version` and stored with every run.
