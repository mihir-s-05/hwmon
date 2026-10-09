# Build and work on the code

To run an existing release, you do not need these tools. This page is for building
or changing hwmon.

## Tools

- .NET 8 SDK. `global.json` selects an installed .NET 8 feature band rather than
  silently switching the project to another major SDK.
- Git.
- PowerShell 7 (`pwsh`) for the shared build/release scripts.
- Python 3.10 or newer for packaging; it uses only the standard library.

## Build and test

From the repository root:

```bash
dotnet restore Hwmon.csproj --locked-mode
dotnet build Hwmon.csproj -c Release --no-restore
```

On Windows:

```powershell
dotnet run --project Hwmon.csproj -f net8.0-windows -c Release -- self-test
dotnet run --project Hwmon.csproj -f net8.0-windows -c Release -- gpu
```

On Linux/WSL:

```bash
dotnet run --project Hwmon.csproj -f net8.0 -c Release -- self-test
dotnet run --project Hwmon.csproj -f net8.0 -c Release -- gpu --native
```

The project has two target frameworks. `net8.0` excludes Windows-only libraries
and backends. `net8.0-windows` enables them. Always specify `-f` with `dotnet run`.
Offline tests need neither a GPU nor administrator access.

## Portable builds

```powershell
pwsh -File scripts/build.ps1 -Target windows
pwsh -File scripts/build.ps1 -Target linux
pwsh -File scripts/release.ps1 -Target all
```

Builds include the .NET runtime, documentation and notices. The build script runs
the published executable's offline checks when its platform matches the build
host, and verifies that its reported version matches the project. Linux execution
is checked on Linux CI; Windows execution is checked on Windows CI.

The default build output is a fresh directory under `artifacts/`. It prints the
path and writes `build.json`. Release archives go into `dist/`. Neither directory
is committed. For one platform you can use `release.ps1 -Target windows` or `linux`.

If Python is not named `python` on your system, pass its actual command/path:

```bash
pwsh -File scripts/release.ps1 -Target linux -PythonExecutable python3
```

## Where things live

| Path | Purpose |
|---|---|
| `Program.cs`, `CliOptions.cs` | Commands and terminal behavior |
| `Engine.cs`, `Models.cs` | Combine backends and describe readings |
| `Backends/` | Direct hardware/OS providers |
| `TelemetryRules.cs` | Quality checks, statistics and diagnostics |
| `Logging.cs` | CSV and JSON output |
| `SelfTests.cs` | Offline behavior and kernel fixtures |
| `scripts/` | Portable builds and release archives |
| `.github/workflows/` | Windows/Linux CI and release jobs |
| `docs/` | Setup guides, reference and validation notes |
| `licenses/` | Third-party texts and corresponding source archives |

Machine-specific captures stay in Git-ignored `local-data/`; they are not shipped
in source archives. The source packager includes tracked files only.

## Updating a dependency or version

Change the version in `Hwmon.csproj`. The CLI's displayed version comes from the
assembly metadata, so it follows that project value. Record changes in CHANGELOG.md.

If you intentionally change NuGet dependencies, run `dotnet restore --force-evaluate`,
review and commit `packages.lock.json`, and update the relevant source/license
archives. Normal builds restore in locked mode. Release packaging reads license
notices from the exact .NET runtime being published.

See [releases](releases.md) for tags, archives and GitHub drafts.
