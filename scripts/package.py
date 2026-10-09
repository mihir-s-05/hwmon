"""Create portable, versioned release archives. Python 3.10+, standard library only."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile

REPO = pathlib.Path(__file__).resolve().parent.parent


def project_version():
    version = ET.parse(REPO / 'Hwmon.csproj').findtext('./PropertyGroup/Version')
    if not version or not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', version):
        raise ValueError('Hwmon.csproj needs a release version such as 0.2.0.')
    return version


def check_bundle(path, executable, version):
    if path is None:
        raise ValueError(f'Missing published folder containing {executable}.')
    path = pathlib.Path(path).resolve()
    for required in [executable, 'hwmon.dll', 'hwmon.runtimeconfig.json', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses']:
        if not (path / required).exists():
            raise ValueError(f'Incomplete published bundle: {path / required}')
    config = json.loads((path / 'hwmon.runtimeconfig.json').read_text(encoding='utf-8-sig'))
    if not config['runtimeOptions'].get('includedFrameworks'):
        raise ValueError('Release must include its .NET runtime (self-contained publish).')
    manifest = path.parent / 'build.json'
    if not manifest.exists() or json.loads(manifest.read_text(encoding='utf-8-sig'))['version'] != version:
        raise ValueError('Build manifest is missing or its version differs from Hwmon.csproj.')
    return path


def tar_modes(info):
    info.uid = info.gid = 0
    info.uname = info.gname = ''
    info.mode = 0o755 if info.isdir() or pathlib.PurePosixPath(info.name).name == 'hwmon' or info.name.endswith('.exe') else 0o644
    return info


def zip_folder(path, source, root):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for file in sorted(source.rglob('*')):
            if file.is_file():
                archive.write(file, str(pathlib.PurePosixPath(root) / file.relative_to(source).as_posix()))


def package(args):
    version = project_version()
    if args.tag and args.tag != 'v' + version:
        raise ValueError(f'Tag {args.tag} does not match project version v{version}.')
    output = pathlib.Path(args.output)
    if not output.is_absolute():
        output = REPO / output
    output.mkdir(parents=True, exist_ok=True)
    prefix = 'hwmon-v' + version
    windows = check_bundle(args.windows, 'hwmon.exe', version) if args.platform in ['windows', 'wsl', 'all'] else None
    linux = check_bundle(args.linux, 'hwmon', version) if args.platform in ['linux', 'wsl', 'all'] else None
    jobs = []
    if args.platform in ['windows', 'all']:
        jobs.append(('windows', output / (prefix + '-windows-x64.zip')))
    if args.platform in ['linux', 'all']:
        jobs.append(('linux', output / (prefix + '-linux-x64.tar.gz')))
    if args.platform in ['wsl', 'all']:
        jobs.append(('wsl', output / (prefix + '-wsl-x64.tar.gz')))
    source_path = output / (prefix + '-source.zip')
    checksum_path = output / (prefix + '-SHA256SUMS.txt')
    for path in [p for _, p in jobs] + [source_path, checksum_path]:
        if path.exists() and not args.force:
            raise FileExistsError(f'{path.name} already exists; use --force to replace release assets.')
    # Resolve the source list before creating archives. Git-ignored captures,
    # build folders and repository internals must never enter a source release.
    listing = subprocess.run(['git', '-C', str(REPO), 'ls-files', '-z'], capture_output=True, check=True).stdout
    tracked = [name.decode('utf8') for name in listing.split(b'\0') if name]
    if not tracked:
        raise ValueError('No tracked source files. Stage the repository before packaging.')
    for platform, path in jobs:
        if platform == 'windows':
            zip_folder(path, windows, 'hwmon-windows')
        else:
            with tarfile.open(path, 'w:gz') as archive:
                archive.add(linux, arcname='hwmon-' + platform, filter=tar_modes)
                if platform == 'wsl':
                    archive.add(windows, arcname='hwmon-wsl/windows', filter=tar_modes)
        print(path)
    with zipfile.ZipFile(source_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name in tracked:
            archive.write(REPO / name, 'hwmon-source/' + name)
    assets = [p for _, p in jobs] + [source_path]
    checksum_path.write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n' for p in assets), encoding='utf8')
    print(source_path)
    print(checksum_path)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', choices=['windows', 'linux', 'wsl', 'all'], default='all')
    parser.add_argument('--windows', type=pathlib.Path)
    parser.add_argument('--linux', type=pathlib.Path)
    parser.add_argument('--output', default='dist')
    parser.add_argument('--tag', help='Optional release tag; must match the project version.')
    parser.add_argument('--force', action='store_true')
    try:
        package(parser.parse_args())
    except (ValueError, FileExistsError, subprocess.CalledProcessError) as error:
        parser.exit(2, 'package: ' + str(error) + '\n')
