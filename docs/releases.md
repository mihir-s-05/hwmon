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

Filenames start with the project version, for example `hwmon-v0.2.1-...`.
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
Get-FileHash .\dist\hwmon-v0.2.1-windows-x64.zip -Algorithm SHA256
```

Or, on Linux, from the `dist` folder:

```bash
sha256sum -c hwmon-v0.2.1-SHA256SUMS.txt
```

GPU-less CI checks the executable and offline behavior. Hardware/driver coverage
still needs live testing when sensor behavior changes; see [validation](validation/README.md).

## Automatic releases from GitHub

Push code to `main` and **Package releases** takes care of the rest. It builds
and tests Windows and Linux, assembles the WSL package, adds source and checksums,
and creates the release tag. You do not need to push a tag yourself.

| Change | Result |
|---|---|
| A routine code push, such as a CSV fix | The next patch version builds and publishes automatically |
| You set a new minor version, such as `0.3.0` | Builds a draft for you to review and publish |
| You set a new major version, such as `1.0.0` | Builds a draft for you to review and publish |
| A documentation-only push | CI runs; it does not create a new release |

The trigger includes C# files, the project, dependency lock file, SDK selection,
build/release scripts and workflows. Pull requests and other branches run CI
without publishing releases.

## Your normal workflow

Pull the latest version before working, make your changes, commit and push:

```bash
git pull --ff-only
# Edit the code and check it.
git add .
git commit -m "Fix GPU logging"
git push origin main
```

If `Hwmon.csproj` still has the last released version, the workflow increments
the patch number. For example, a new code push after `0.2.1` becomes `0.2.2`.
It commits the new version and a changelog entry back to `main`, then builds
that commit. Pull again before your next change so you have the bot's version
commit locally. The built CLI, archive names and Git tag all use that version.

You can set the next patch version and write its changelog yourself if you
prefer. The workflow uses that version when it is newer than the existing tags.
You can also add an `Unreleased` section to CHANGELOG.md; the workflow turns
it into the next version's notes instead of creating a generic entry.

## Review a bigger release

When a change deserves a minor or major version, update `Version` in
Hwmon.csproj, add the matching CHANGELOG.md section, then commit and push.
For example, change `0.2.1` to `0.3.0` for a new minor release.

The workflow uploads a complete draft to GitHub Releases. Review its notes and
assets, then click **Edit** and **Publish release**. Patches in that new minor
version also stay drafts until you publish a release in that version. Once
`0.3.0` is published, subsequent `0.3.x` patches can publish automatically.

To require review for a patch as well, put `[release-review]` in the commit
message before pushing it:

```bash
git commit -m "Change GPU diagnostics [release-review]"
git push origin main
```

## Manual builds and retries

In Actions, open **Package releases**, choose **Run workflow**, and select a mode:

- **artifacts** is the default. It builds downloads without changing versions,
  creating tags or publishing a release. This also works on another branch.
- **automatic** applies the normal version and review rules on `main`.
- **draft** requires review even for a patch on `main`.

If a build fails, fix the cause or re-run the failed workflow. Publication
requires both platform builds and their self-checks to pass. Assets are uploaded
to a draft before an automatic patch is made public. A retry can complete an
interrupted upload; a successful push is not published twice.

Release runs are serialized and build the latest `main` commit. When pushes
arrive quickly, one release can include several commits. A queued run skips
publication if those same changes have already been released. Existing published
release assets are preserved.

Automatic publication uses stable `major.minor.patch` versions. Use artifacts
mode for a prerelease version such as `0.3.0-beta.1`.

## How it is checked

The workflow runs the release-policy tests, builds both executables, runs their
offline self-checks, verifies that NuGet did not change the dependency lock, and
checks the version while packaging. Code or build failures stop publication.
CI also runs the policy tests for pull requests.

The workflows use commit-pinned official actions. Build jobs have read-only
repository access. The version-preparation and release jobs have write access
to update the version and upload the release using GitHub's built-in token;
you do not need to create a personal access token. Bot pushes made with that
token do not start recursive workflows, as explained in
[GitHub's workflow-trigger guide](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).

If you later protect `main` against bot pushes, the version commit may fail.
Keep that protection if you want it and use your repository's approved merge
process to commit the next version explicitly.

For downloadable Actions artifacts, see
[GitHub's guide](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).
The release steps use [GitHub CLI](https://cli.github.com/manual/gh_release_create).
