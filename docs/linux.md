# Linux setup

The Linux package is for x86_64 systems using glibc. It includes its .NET runtime.
The live remote test used Ubuntu 24.04 under WSL2; physical Linux hwmon sensor
handling was tested with fixtures rather than a bare-metal machine.

## Extract and run

From the directory containing the downloaded archive:

```bash
tar -xzf hwmon-v0.2.1-linux-x64.tar.gz
cd hwmon-linux
./hwmon doctor
./hwmon gpu
./hwmon cpu
```

The archive preserves the executable bit. Keep the extracted folder together.

## NVIDIA GPU access

On native Linux, install the NVIDIA driver using your distribution's normal
driver instructions. hwmon loads `libnvidia-ml.so.1` from the system loader.
Installing a CUDA toolkit is not required just to read NVML sensors.

If `nvidia-smi` works but hwmon does not, start with `./hwmon doctor`; it reports
the loaded NVML library and any initialization error. hwmon reads NVML directly
and does not use `nvidia-smi` as its sensor backend.

WSL has a different driver setup. If this is WSL, use [the WSL guide](wsl.md)
instead of installing a Linux display driver.

## CPU and other sensors

The Linux provider reads CPU counters and reported frequencies, memory,
interface throughput/errors, and block-device activity from `/proc`. It also
reads battery attributes and any sensor chips exposed in `/sys/class/hwmon`.

Which physical sensors appear depends on your kernel drivers. Driver labels and
standard unit conversions are preserved; board-specific calibration from
`sensors.conf` is not applied. hwmon does not install kernel modules or write
hardware-control attributes.

## Optional: add it to your PATH

After moving the extracted folder to a permanent location:

```bash
mkdir -p ~/.local/bin
ln -s "$(pwd)/hwmon" ~/.local/bin/hwmon
```

If the name already exists, inspect it before replacing it. Make sure
`~/.local/bin` is on your PATH, then open a new terminal and try `hwmon doctor`.
The symlink must continue to point to the executable's complete folder.

See [coverage](coverage.md) for platform differences and [troubleshooting](troubleshooting.md)
for missing libraries or sensors.
