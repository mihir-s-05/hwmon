# Coverage and limits

hwmon combines several sources because each exposes a different part of the
hardware. The `source` field tells you which interface supplied a channel.

| Capability | Windows | Native Linux/WSL | WSL with Windows helper |
|---|---|---|---|
| NVIDIA core temperature, power, clocks and VRAM | Yes, when supported | NVML | Native plus host sources |
| Hotspot and memory-junction temperature | Complementary Windows queries | Often absent from NVML; depends on driver/hwmon | Windows queries, when supported |
| Individual NVIDIA rails and crossbar clock | Isolated private NVAPI worker | Not supplied by the Windows-specific backend | Windows worker through interop |
| GPU limiting reasons | NVAPI and NVML, with distinct labels | Supported NVML clock-event flags | Both sets |
| CPU, memory, network and disk activity | Windows providers/library | Kernel counters | VM counters plus host GPU data |
| Physical CPU/board temperatures | Driver/access dependent | Available hwmon drivers | Commonly absent from the WSL kernel |

“Yes” means the interface is implemented, not that every driver supports every
query. Use `sensors --all` to see status for exposed channels.

## Tested hardware

- Windows laptop: ASUS GU604VI, i9-13900H, Intel Iris Xe, RTX 4070 Laptop GPU,
  NVIDIA driver 617.14.
- WSL desktop: Ubuntu 24.04 WSL2, RTX 3080 10GB, Windows driver 591.86.

On the laptop, 41 of 43 NVIDIA columns from the reference HWiNFO log had mapped
measurements. Two group summaries had comparable derived results rather than
verified identical aggregation. See [the mapping](validation/hwinfo-coverage.csv).

The WSL desktop exposed 52 native GPU channels and 238 combined channels. These
counts include separate API sources and unavailable channels; they are not counts
of unique physical sensors. See [the validation notes](validation/README.md).

## Differences worth understanding

The HWiNFO-style memory clock division and rail-average channel lists were
verified for the RTX 4070 Laptop. They are not automatically applied to other
models. Unknown rail identifiers are preserved rather than assigned invented names.

The “16-pin HVPWR” label follows HWiNFO's naming for a driver rail ID. The label
alone does not prove that a laptop or board has that physical connector.

Fan RPM can belong to a laptop's embedded controller rather than its GPU driver.
CPU registers and SMART/reliability information can require additional access.
Some vendor-specific HWiNFO sensors are not reproduced here. Encoder FPS is
encoding activity, not game presentation FPS.

The private NVIDIA worker has an eight-second timeout and is disabled for the
session after a crash/timeout. Driver updates can change those interfaces.
`--no-rails` skips it. The WSL helper has a separate 25-second timeout.

Multiple identical NVIDIA GPUs have not been validated. UUID/PCI metadata are
used where available, and ambiguous name joins generate a diagnostic. Cross-driver
accuracy, physical power calibration and a 12-hour endurance run were not tested.

For implementation evidence, see [the NVIDIA interface notes](internal/nvidia.md).
