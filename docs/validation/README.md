# Validation notes

These are measurements from specific machines, not a promise that every sensor
works on every GPU or operating system.

- [Windows laptop](windows.md): original RTX 4070 Laptop coverage and later regression checks.
- [Linux/WSL desktop](linux-wsl.md): Ubuntu 24.04 WSL2 with an RTX 3080, tested at idle and while load appeared.
- [HWiNFO column mapping](hwinfo-coverage.csv): mapped and comparable NVIDIA fields from the laptop log.

The application has 20 offline checks covering accepted/rejected statistics,
CSV formatting, output collision protection, Linux units, memory accounting,
CPU counter deltas, counter resets and faulted hwmon readings. CI runs those on
both Windows and Linux without requiring a GPU.

## Release packaging checks

The v0.2.0 release scripts were run locally with the .NET 8 SDK. The extracted
Windows and Linux packages reported the correct version and each passed all
20 offline checks. The Linux executable was run over SSH on the WSL2 desktop.

The combined WSL archive was also extracted on that desktop. Its Windows helper
ran through WSL interop, passed the 20 checks, and was detected automatically by
the Linux CLI. A live GPU snapshot contained 238 channels, including 186 from
the host helper, with no backend initialization errors.

Archive checks verified SHA-256 checksums, ZIP integrity, Linux executable
permissions, the WSL folder layout, exclusion of local captures and build output
from the source archive, and protection against overwriting existing assets or
packaging a mismatched version tag. Both GitHub workflows passed actionlint
1.7.12. The pinned action commits match their official release tags.

GitHub-hosted workflows will run after the repository is pushed to GitHub; the
local checks above do not represent a hosted Actions run.

Physical Linux hwmon discovery was fixture-tested; the live Linux execution was
WSL2. No 12-hour endurance run or electrical power calibration was performed.
See [coverage](../coverage.md) for the practical limits.
