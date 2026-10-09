# Troubleshooting

Start with `hwmon doctor`, then `hwmon sensors --all`. The doctor command checks
platform/library access; each sensor's note explains its own failure or status.

## The GPU is missing

On Windows, check the installed NVIDIA driver. On native Linux, check that the
NVIDIA driver and `libnvidia-ml.so.1` are available. In WSL, try:

```bash
/usr/lib/wsl/lib/nvidia-smi
```

If that cannot see the GPU, fix WSL/Windows driver access first. See [WSL setup](wsl.md).
If no NVIDIA GPU exists, CPU and other providers can still supply system readings.

## CPU temperatures or power are missing

On Windows, read `doctor`'s PawnIO and administrator results. See [Windows setup](windows.md).
On native Linux, physical sensors depend on `/sys/class/hwmon` drivers and access.
In WSL, physical CPU sensors are commonly unavailable; CPU usage can still work.

## Windows helper fails in WSL

Run `./windows/hwmon.exe --version` from the WSL package. If it fails, confirm that
the whole Windows subfolder was extracted and interop is enabled. For a custom
helper use a Linux path such as `/mnt/c/Tools/hwmon-windows/hwmon.exe`, not `C:\...`.
`--native` skips the helper while you troubleshoot.

## An interval of one second produces slower samples

The interval is a target. Hardware queries, warmup and helper startup can take
longer. Rich WSL snapshots averaged about four seconds on the tested desktop.
Use native mode for lighter polling and inspect `sampledAt` for actual timing.

## A value looks impossible

Check its source, status, unit and note. Driver-returned power spikes and D3D
percentages above the expected range have been observed. Rejected values remain
as raw data and are excluded from accepted statistics; the CLI does not replace
them with zero. Compare like units and sample times across sources.

## A log will not start

Make sure its parent directory exists and you can write there. Existing files
are preserved. Choose a new filename, or use `--force` if you intend to replace it.

## Wide CSV stopped after a device/channel change

Its initial inventory defines the columns. New channels cannot be represented
silently in that header, so the CLI stops and keeps completed rows. Use ordinary
long CSV or JSONL for changing inventories.

## The executable will not run after I moved it

Move or extract the complete package folder, including the runtime files. WSL's
rich mode also needs the complete `windows` subfolder. The Linux package is x64
and glibc-based; it is not an ARM64 or Alpine/musl build.

When reporting a problem, include OS/driver versions, `doctor` output, the exact
command and a relevant sensor note. Review logs before sharing them: exports can
contain device identifiers and local paths.
