# Make a release

The same scripts create local release files and GitHub Actions artifacts.
Each release can contain:

| File suffix | Contents |
|---|---|
| `-windows-x64.zip` | Windows executable, runtime, docs and notices |
| `-linux-x64.tar.gz` | Linux executable, runtime, docs and notices |
| `-wsl-x64.tar.gz` | Linux package with a complete Windows helper at `windows/` |
| `-source.zip` | Tracked source, build scripts, docs and notices |
| `-SHA256SUMS.txt` | SHA-256 checksums of the release archives |

Filenames start with the project version, for example `hwmon-v0.2.0-...`.
The application and archives keep library DLLs separate so they can be replaced.
NVIDIA driver DLLs and HWiNFO executables are not shipped.

## Build locally

From a Git checkout with .NET 8, PowerShell 7 and Python 3.10+:

```powershell
pwsh -File scripts/release.ps1 -Target all
```

This publishes both platforms, runs the executable's offline checks for the
current host, then creates Windows, Linux, WSL and source archives in `dist/`.
The packaging script restores executable permissions in Linux/WSL tar archives,
including when they are assembled on Windows.

For only one platform, select `-Target windows` or `-Target linux`. If Python is
called `python3`, use `-PythonExecutable python3`. Existing release files are
preserved; `-Force` deliberately replaces generated assets in the output folder.

To package already-built folders, use `scripts/package.py --help`. Each folder
must have the `build.json` generated beside it by the build script. The packager
checks the project version and whether the runtime is included.

## Check the files

Extract the Windows archive on Windows and the Linux archive on Linux/WSL. Run
`--version` and `self-test` from the extracted copies. Check the WSL archive has
its `windows` folder and that `./windows/hwmon.exe --version` works in WSL.

You can verify one archive against the corresponding line in SHA256SUMS.txt:

```powershell
Get-FileHash .\dist\hwmon-v0.2.0-windows-x64.zip -Algorithm SHA256
```

Or, on Linux, from the `dist` folder:

```bash
sha256sum -c hwmon-v0.2.0-SHA256SUMS.txt
```

GPU-less CI checks the executable and offline behavior. Hardware/driver coverage
still needs live testing when sensor behavior changes; see [validation](validation/README.md).

## Use GitHub Actions

Push this repository to your own GitHub repository. **Build and test** runs on
Windows and Linux for pushes to `main` and pull requests. **Package releases**
can also be run manually from the Actions tab to produce downloadable artifacts.

To prepare a versioned GitHub release:

1. Update `Version` in Hwmon.csproj and CHANGELOG.md, then commit the changes.
2. Tag that commit with the same version prefixed by `v`.
3. Push the commit and tag.

For a future version, the commands look like this:

```bash
git tag -a v0.2.1 -m "hwmon 0.2.1"
git push origin main
git push origin v0.2.1
```

Use those only after the project version has been changed to `0.2.1`. The
packager rejects a tag that differs from the project version.

The tag workflow builds on both platforms, combines the WSL bundle, attaches
checksums and source, and creates a **draft GitHub release**. Review its notes and
assets on GitHub, then publish it. A manual workflow run produces artifacts but
does not create a version tag or release. Re-running a tag that already has a
release fails instead of replacing its assets silently.

The workflows use commit-pinned official actions. Build jobs have read-only
repository permissions; only the draft-release job receives write access. Hosted
GitHub runners are used; no separate personal access token is needed.

For GitHub artifact downloads, see [GitHub's guide](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).
The draft step uses [GitHub CLI release create](https://cli.github.com/manual/gh_release_create).
