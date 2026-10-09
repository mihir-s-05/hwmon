# WSL setup

The WSL package gives you a Linux command with access to additional Windows GPU
measurements. Its Windows helper reads the host driver directly; it does not
open or control HWiNFO or another GUI.

If WSL and NVIDIA GPU access already work, skip to **Install hwmon**.

## 1. Check that you are using WSL2

In Windows PowerShell:

```powershell
wsl --list --verbose
```

Look for `2` in the VERSION column beside your distribution. If WSL is not
installed, open PowerShell as administrator and run:

```powershell
wsl --install -d Ubuntu
```

Restart if requested, then open Ubuntu and finish creating its Linux user account.
For installation details and converting an existing WSL1 distribution, follow
[Microsoft's WSL guide](https://learn.microsoft.com/en-us/windows/wsl/install).

## 2. Check NVIDIA access inside WSL

Install an NVIDIA Windows driver that supports WSL GPU access. Then, inside your
Linux terminal, run:

```bash
/usr/lib/wsl/lib/nvidia-smi
```

You should see your GPU. This location is normal in WSL even when `nvidia-smi`
is not on PATH. hwmon also knows where to find the WSL NVML library.

Use the Windows NVIDIA driver for WSL GPU support; do not install a Linux NVIDIA
display driver inside WSL. NVIDIA explains the driver setup and telemetry limits
in its [WSL guide](https://docs.nvidia.com/cuda/wsl-user-guide/index.html).

## 3. Install hwmon

Download the **WSL** archive and open a WSL terminal in the folder containing it:

```bash
tar -xzf hwmon-v0.2.0-wsl-x64.tar.gz
cd hwmon-wsl
./hwmon doctor
./hwmon gpu
```

If you downloaded it through Windows, your Downloads folder is usually available
under `/mnt/c/Users/YOUR_WINDOWS_USERNAME/Downloads`. Replace the username with
your Windows account name. You can copy the archive into your Linux home folder
before extracting it.

Keep this layout intact:

```text
hwmon-wsl/
  hwmon                 Linux executable
  hwmon.dll             Linux application
  ...                   Linux runtime and documentation
  windows/
    hwmon.exe           Windows helper
    ...                 Windows libraries and runtime
```

`./hwmon gpu` detects `windows/hwmon.exe` automatically. The source label
`Windows host / ...` identifies a host reading. Native and host channels have
separate IDs and statistics, and each reading records its sampling time.

## 4. Choose the amount of detail you need

```bash
./hwmon gpu                         # Native plus host GPU measurements
./hwmon gpu --native                # Native NVML only
./hwmon sensors --gpu --all          # Include unavailable/unsupported channels
./hwmon log gpu.csv --gpu --duration 1m
```

The helper adds measurements such as hotspot and memory-junction temperatures,
voltage, individual rails, crossbar clock and NVAPI limiter flags, where the
Windows driver provides them. Native mode has fewer GPU channels but is lighter.

On the tested desktop, a rich poll took about four seconds even with a one-second
requested interval. Polls remain sequential rather than overlapping. Use
`--native` when faster sampling matters more than the additional host channels.

WSL CPU, RAM, network and disk counters describe the **Linux VM**, not the host's
physical CPU sensors or drives. The helper enriches GPU data; it does not change
that distinction.

## Use an existing Windows folder instead

You can use the Linux package with a separate Windows release:

```bash
./hwmon gpu --windows-helper /mnt/c/Tools/hwmon-windows/hwmon.exe
```

That path is an example; use the real path to your extracted Windows folder.
The entire Windows folder must remain together. The helper must be runnable
through WSL interop.

## If the helper will not start

Try running it directly from the WSL package:

```bash
./windows/hwmon.exe --version
```

If that fails, check that the Windows folder is complete and Windows executable
interop is enabled for the distribution. hwmon reports a helper failure and
continues with native providers. If it hangs beyond 25 seconds, it disables the
helper for that session. `--native` skips it entirely.

You do not need to modify WSL configuration when this direct command works.
For interop settings, use [Microsoft's wsl.conf reference](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#interop-settings).
For other failures, see [troubleshooting](troubleshooting.md).

## Optional: use `hwmon` from any folder

After moving the complete `hwmon-wsl` folder to a permanent location:

```bash
mkdir -p ~/.local/bin
ln -s "$(pwd)/hwmon" ~/.local/bin/hwmon
```

If that name already exists, inspect it first. Make sure `~/.local/bin` is on
PATH. Keep the `windows` subfolder beside the Linux executable after installing.
