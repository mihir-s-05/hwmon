hwmon reads hardware measurements directly and makes them available in your terminal,
CSV logs or JSON. The portable packages include the .NET runtime.

Choose the archive for your platform:

- **windows-x64.zip**: Windows CLI with the full Windows GPU backend.
- **linux-x64.tar.gz**: native Linux CLI using NVML and kernel telemetry.
- **wsl-x64.tar.gz**: Linux CLI plus the Windows helper for richer WSL GPU readings.
- **source.zip**: source, documentation, build scripts and license notices.
- **SHA256SUMS.txt**: checksums for the release archives.

Start with `hwmon gpu`, `hwmon doctor` or the README in the extracted folder.
See CHANGELOG.md for changes and docs/wsl.md for WSL setup.

Sensor availability depends on your hardware, driver and permissions. The richer
WSL path can take a few seconds per poll; `--native` uses the lighter Linux path.
See docs/coverage.md for the tested coverage and remaining gaps.
