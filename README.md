# hwmon

Hardware readings in your terminal, with CSV and JSON logging.

Use hwmon to check GPU temperatures, clocks, memory and power, see why a GPU is
being limited, or record measurements while a workload runs. It also reads CPU,
memory, battery, network and disk information where the operating system exposes it.

It talks to hardware drivers and operating-system interfaces directly. HWiNFO
does not need to be running. There is no GUI automation or 12-hour monitoring timer.
hwmon reads measurements; it does not change fan curves, clocks or power limits.

## Choose a package

Release packages include the .NET runtime. You do not need to install .NET to run them.
Keep the extracted folder together; the executable needs its accompanying files.

| Your system | Package | What to expect |
|---|---|---|
| Windows x64 | `hwmon-v0.2.0-windows-x64.zip` | The full Windows sensor backend |
| Linux x64 | `hwmon-v0.2.0-linux-x64.tar.gz` | NVIDIA NVML plus Linux CPU, memory, network, disk and available hwmon sensors |
| WSL2 x64 | `hwmon-v0.2.0-wsl-x64.tar.gz` | The Linux CLI plus a Windows helper for additional GPU measurements |

Look for these assets on the repository's Releases page, or build them locally
using [the release guide](docs/releases.md). For another version, substitute its
version number in the filenames below.

## Start on Windows

Open PowerShell in the folder containing the downloaded ZIP:

```powershell
Expand-Archive .\hwmon-v0.2.0-windows-x64.zip -DestinationPath .
cd .\hwmon-windows
.\hwmon.exe gpu
.\hwmon.exe doctor
```

[Windows setup and CPU access](docs/windows.md) explains optional driver and
administrator requirements.

## Start on Linux

Open a terminal in the folder containing the archive:

```bash
tar -xzf hwmon-v0.2.0-linux-x64.tar.gz
cd hwmon-linux
./hwmon gpu
./hwmon doctor
```

NVIDIA GPU readings require the NVIDIA Linux driver. CPU and other system readings
come from the kernel. See [Linux setup](docs/linux.md).

## Start in WSL

Use the WSL package if you want the extra Windows GPU measurements:

```bash
tar -xzf hwmon-v0.2.0-wsl-x64.tar.gz
cd hwmon-wsl
./hwmon gpu
./hwmon gpu --native
```

The first command uses the bundled Windows helper automatically. The second uses
only the lighter Linux/WSL providers. Follow [the WSL setup guide](docs/wsl.md) if
you need to install WSL, enable GPU access, or troubleshoot Windows interop.

## Everyday commands

These examples use Linux/WSL spelling. On Windows, replace `./hwmon` with `.\hwmon.exe`.

```bash
./hwmon gpu                                    # A readable GPU overview
./hwmon sensors --gpu --all                     # Every GPU channel and its status
./hwmon watch --gpu --interval 1s               # Keep refreshing; Ctrl+C stops
./hwmon log gpu.csv --gpu --duration 30s         # Record a short CSV
./hwmon log gpu.jsonl --gpu --format jsonl       # Record continuously as JSON
./hwmon export snapshot.json                    # Save one complete snapshot
./hwmon diag --gpu                              # Explain active diagnostic findings
```

Leave off `--duration` and `--samples` to log until you stop the process. Existing
files are preserved unless you explicitly use `--force`.

## Know what a reading means

Every channel carries its unit, source and status. An unavailable value stays
blank/null rather than becoming zero. Implausible values are marked invalid and
kept as raw data, and only accepted readings enter minimum/maximum/average statistics.

Sensor coverage depends on your GPU, driver, platform and permissions. Windows
and the WSL helper expose NVIDIA details that native Linux NVML may not provide,
such as individual rails, crossbar clock and extra temperature sensors. hwmon
does not claim complete HWiNFO parity on every machine.

WSL CPU, RAM and disk counters describe the virtual machine. The richer WSL GPU
path took about four seconds per poll on the tested desktop; `--native` is lighter.
The requested interval is a target, and actual sample times are recorded.

Read [coverage and limitations](docs/coverage.md) or [logging formats](docs/logging.md)
when comparing readings or consuming them in another program.

## Documentation

- [All commands and examples](docs/usage.md)
- [Windows setup](docs/windows.md), [Linux setup](docs/linux.md), [WSL setup](docs/wsl.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Build from source](docs/development.md)
- [Create Windows, Linux and WSL releases](docs/releases.md)
- [Validation results](docs/validation/README.md)

## License

The application is MIT-licensed. Windows builds and the WSL helper use separately
licensed libraries; their DLLs remain replaceable. See [LICENSE](LICENSE) and
[third-party notices](THIRD-PARTY-NOTICES.md). Release packages include license
texts and corresponding source archives. NVIDIA drivers and HWiNFO binaries are
not included.
