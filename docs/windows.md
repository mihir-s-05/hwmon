# Windows setup

Use the Windows x64 ZIP. It includes the executable and .NET runtime, so there is
no hwmon installer to run.

## Extract and try it

Open PowerShell in your Downloads folder, or wherever you saved the archive:

```powershell
Expand-Archive .\hwmon-v0.2.1-windows-x64.zip -DestinationPath .
cd .\hwmon-windows
.\hwmon.exe gpu
.\hwmon.exe doctor
```

Move the whole `hwmon-windows` folder if you want to store it elsewhere. Copying
only `hwmon.exe` will leave behind required libraries and runtime files.

For NVIDIA measurements, use the NVIDIA driver installed on the machine. HWiNFO
can be closed. Standard GPU readings do not require an administrator terminal on
the tested machines.

## Record a workload

```powershell
.\hwmon.exe watch --gpu
.\hwmon.exe log gpu.csv --gpu --interval 1s --duration 1m
.\hwmon.exe export snapshot.json
```

Run these separately. `watch` stays active until Ctrl+C. To record for as long as
you need, omit `--duration` from `log`; Ctrl+C stops and flushes the file.

## Get more CPU measurements

CPU load works without low-level register access. CPU temperatures, power,
voltages and some clocks may need the official [PawnIO driver](https://pawnio.eu/)
and an administrator terminal with this LibreHardwareMonitor version.

Run `doctor` first. If you want those additional readings, install PawnIO from its
official site, open PowerShell as administrator, and run hwmon again. Installing
PawnIO is a separate choice; hwmon does not install a driver for you.

Disk SMART/reliability data can also require elevation. Availability depends on
the Windows storage provider. Ordinary disk activity counters can still work when
SMART access is denied.

## Optional: make `hwmon` available everywhere

Add the folder containing `hwmon.exe` to your **user Path** through Windows
Environment Variables, then open a new terminal. You can then type `hwmon gpu`
from any folder. This is optional; the relative commands above work as they are.

See [troubleshooting](troubleshooting.md) for missing sensors or driver errors.
