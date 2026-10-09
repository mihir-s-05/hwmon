# hwmon 0.2.0 Linux / WSL validation

Tested by SSH to `desktop` on October 9, 2026. The SSH target is Ubuntu 24.04.3
under WSL2, kernel 6.6.87.2-microsoft-standard-WSL2, x86_64. GPU: NVIDIA GeForce
RTX 3080 10GB. Windows NVIDIA driver: 591.86. The WSL library/tool reports 590.57.
The CLI used `/usr/lib/wsl/lib/libnvidia-ml.so.1` directly. No .NET installation,
driver installation, hardware-control change, GUI operation or synthetic stress
workload was required.

## Live results

- Native WSL GPU mode: 52 channels; final tested status counts
  `{'ok': 45, 'unsupported': 4, 'unavailable': 3}`.
- Optional Windows host mode: 238 GPU channels including extra temperatures,
  crossbar, voltage, six NVAPI limiter flags, D3D engines and individual rails.
  Initial idle snapshot: 221 OK, 12 unsupported, 5 unavailable. The remaining
  unavailable items include laptop-specific derived rail averages that are not
  applied to this RTX 3080.
- Native whole-system mode: 117 channels across nine device entries, including
  WSL CPU, memory, network and virtual block devices. No hwmon chip is exposed
  by this WSL kernel, so physical CPU/board sensor access was not tested live.
- Optional helper worked from the WSL filesystem with Windows interop and
  was detected automatically from `windows/hwmon.exe`.
- Installed the CLI on the test user's PATH with the Windows helper beside it.

## Idle and changing-load observations

Initial direct nvidia-smi checks: GPU 45°C, 0–1% utilization, approximately
17–19W, P8, 457MiB reported VRAM. Initial host helper: hotspot 53.3125°C,
memory junction 50°C, crossbar 600MHz.

Load appeared during validation. Reference GPU utilization ranged
7–53%.
A later native CLI check showed 43%, 56°C and 77.376W; the immediately following
nvidia-smi query showed 43%, 56°C and 77.39W. These were separate calls, not a
hardware-synchronized measurement pair. The automatic-helper snapshot contained:

```json
[
  {
    "metric": "GPU Core load",
    "value": 40,
    "unit": "%",
    "source": "Windows host / LibreHardwareMonitor"
  },
  {
    "metric": "GPU Hot Spot temperature",
    "value": 67.59375,
    "unit": "\u00b0C",
    "source": "Windows host / LibreHardwareMonitor"
  },
  {
    "metric": "GPU Memory Junction temperature",
    "value": 66,
    "unit": "\u00b0C",
    "source": "Windows host / LibreHardwareMonitor"
  },
  {
    "metric": "GPU Package power",
    "value": 77.47599792480469,
    "unit": "W",
    "source": "Windows host / LibreHardwareMonitor"
  },
  {
    "metric": "GPU Crossbar Clock",
    "value": 975,
    "unit": "MHz",
    "source": "Windows host / NVAPI GetAllClocks"
  }
]
```

## Verification

- 20 offline checks passed on both Windows and the remote Linux executable.
  Added tests cover kernel units, memory accounting, CPU counter deltas, counter
  resets and faulted hwmon readings.
- 12 native JSONL samples and four rich host JSONL samples were checked for
  valid JSON, unique IDs, finite accepted readings, independently reconstructed
  minimum/maximum/average, and exact accepted sample counts.
- Long CSV: 351 rows across three whole-system snapshots.
  Version 0.2 appends `sampledAt` as the 16th column.
- Wide CSV: two GPU samples, 157 columns; header/data widths matched.
- Timed watch exited cleanly; existing-file protection returned exit code 2.
- Auto-helper export contained both backends with no backend initialization error.
- Windows regression export still exposed 144 GPU channels with all four
  original backends initialized; every reading had a per-backend sample time.

Paired comparisons against nvidia-smi, using nearest samples within 1.2 seconds:

| Metric | Pairs | Mean absolute difference | Maximum absolute difference |
|---|---:|---:|---:|
| temperature.core | 12 | 0.5 | 3 |
| load.gpu | 12 | 9 | 54 |
| memory.used | 12 | 0.71875 | 0.71875 |
| power.reported | 12 | 5.14117 | 17.591 |
| clock.graphics | 12 | 7.5 | 90 |

Units are °C, W, percentage points, MHz and MiB according to the metric.
GPU temperature, clock, power and utilization can change between calls and use
different internal sampling windows. These comparisons verify plausible driver
telemetry and units; they do not calibrate accuracy under load. The constant
0.71875MiB VRAM difference is within nvidia-smi's integer-MiB display precision.

Three host D3D engine load readings exceeded the expected percentage range
(124.019–148.780%). They were retained as raw data, marked invalid and excluded
from statistics. Native NVML utilization remained separately available. The
raw captures are retained locally and excluded from the Git repository.

## Timing and platform limits

The tested Windows-helper polls averaged 3.900s
between complete rich snapshots with a 1s requested interval. This includes
Windows startup, warmup and driver queries. Choose `--native` for lighter/faster
polling. The interval remains a target and polls never overlap. Per-reading
`sampledAt` and source labels distinguish native and host collection times.

The live Linux test was WSL2, not a bare-metal Linux installation. Native Linux
uses the same NVML implementation and a /proc/sysfs provider; physical hwmon
discovery and units were fixture-tested. WSL CPU/RAM/disk values describe the VM.
The Windows helper enriches GPU telemetry and requires WSL interop; it cannot
run on native Linux. Linux-only NVML coverage does not include Windows NVAPI
rails/crossbar and may not expose hotspot or memory-junction temperature.
Read driver-returned status for every channel. No 12-hour endurance test or
electrical-power calibration was performed. There is no licensing timer.

Primary interface references: [NVIDIA NVML](https://docs.nvidia.com/deploy/nvml-api/latest/index.html),
[NVIDIA WSL guide](https://docs.nvidia.com/cuda/wsl-user-guide/index.html),
[kernel hwmon ABI](https://docs.kernel.org/hwmon/sysfs-interface.html),
[kernel disk counters](https://docs.kernel.org/admin-guide/iostats.html).
