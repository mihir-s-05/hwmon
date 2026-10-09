# Changelog

## 0.2.0

- Added a Linux executable that reads NVIDIA NVML and Linux kernel telemetry directly.
- Added an optional Windows helper for WSL, with automatic detection in the WSL bundle.
- Added per-reading timestamps so native and Windows-host measurements stay distinguishable.
- Added Linux counter-reset and sensor-fault handling.
- Added Windows/Linux CI, versioned release packaging, checksums and a WSL setup guide.

## 0.1.0

- Initial Windows sensor, logging, export and diagnostic CLI.
- Combined LibreHardwareMonitor, NVML and NVAPI with isolated NVIDIA rail/crossbar queries.
- Preserved source, status and raw values; excluded invalid readings from session statistics.
- Compared NVIDIA sensor coverage with a recorded HWiNFO log on an RTX 4070 Laptop GPU.
