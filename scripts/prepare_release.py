"""Plan automatic patch releases; keep new minor/major versions as drafts."""
import argparse
import json
import os
import re
import subprocess

from package import REPO, project_version


def run(*args):
    return subprocess.check_output(args, cwd=REPO, text=True).strip()


def version_tuple(value):
    match = re.fullmatch(r'v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', value)
    if not match:
        raise ValueError(f'Automatic releases need a stable version such as 0.2.1: {value}')
    return tuple(map(int, match.groups()))


def stable_version(value):
    try:
        return version_tuple(value)
    except ValueError:
        return None


def source_marker(sha):
    if not re.fullmatch(r'[0-9a-f]{40}', sha):
        raise ValueError('Source commit must be a full Git SHA.')
    return f'<!-- hwmon-source:{sha} -->'


def source_commit(main_sha, message):
    if message.startswith('chore: set release version '):
        marker = re.search(r'^hwmon-source:([0-9a-f]{40})$', message, re.M)
        if marker:
            return marker.group(1)
    return main_sha


def plan_release(declared, releases, tags, source_sha, force_draft=False, reuse_tag=False):
    published = [v for release in releases if not release['draft'] and not release.get('prerelease')
                 and (v := stable_version(release['tag_name'])) is not None]
    baseline = max(published, default=None)

    def requires_review(version):
        return force_draft or baseline is None or version_tuple(version)[:2] != baseline[:2]

    marker = source_marker(source_sha)
    for release in releases:
        if marker in (release.get('body') or ''):
            # A failed upload leaves a draft that can be completed on a rerun.
            version = release['tag_name'].removeprefix('v')
            return {'version': version, 'draft': requires_review(version) if release['draft'] else False,
                    'action': 'resume' if release['draft'] else 'skip'}
    current = version_tuple(declared)
    used = {v for tag in tags if (v := stable_version(tag)) is not None}
    used.update(v for release in releases if (v := stable_version(release['tag_name'])) is not None)
    highest = max(used, default=None)
    if highest is not None and current <= highest and not reuse_tag:
        if current[:2] != highest[:2]:
            raise ValueError('Project version is behind a newer minor/major tag. Update Hwmon.csproj first.')
        current = (highest[0], highest[1], highest[2] + 1)
    version = '.'.join(map(str, current))
    return {'version': version, 'draft': requires_review(version), 'action': 'release'}


def record_version(version, sha):
    project = REPO / 'Hwmon.csproj'
    contents, count = re.subn(r'<Version>[^<]+</Version>', f'<Version>{version}</Version>',
                             project.read_text(encoding='utf8'))
    if count != 1:
        raise ValueError('Expected exactly one Version property in Hwmon.csproj.')
    project.write_text(contents, encoding='utf8', newline='\n')
    changelog = REPO / 'CHANGELOG.md'
    contents = changelog.read_text(encoding='utf8')
    if not re.search(rf'^## {re.escape(version)}[ \t]*$', contents, re.M):
        if re.search(r'^## Unreleased[ \t]*$', contents, re.M):
            contents = re.sub(r'^## Unreleased[ \t]*$', f'## {version}', contents, count=1, flags=re.M)
        else:
            entry = f'## {version}\n\n- Automatic patch release with the code changes from commit `{sha[:7]}`.\n\n'
            contents = contents.replace('# Changelog\n\n', '# Changelog\n\n' + entry, 1)
        changelog.write_text(contents, encoding='utf8', newline='\n')


def release_notes(version, changelog, guide, sha):
    section = re.search(rf'^## {re.escape(version)}[ \t]*\n(.*?)(?=^## |\Z)', changelog, re.M | re.S)
    changes = section.group(1).strip() if section else f'Code changes from commit `{sha[:7]}`.'
    return f'{changes}\n\n{guide.strip()}\n\n{source_marker(sha)}\n'


def write_outputs(plan, source_sha):
    plan.update(tag='v' + plan['version'], sha=run('git', 'rev-parse', 'HEAD'), source_sha=source_sha)
    output = os.environ.get('GITHUB_OUTPUT')
    if output:
        with open(output, 'a', encoding='utf8') as file:
            for key, value in plan.items():
                file.write(f'{key}={str(value).lower() if isinstance(value, bool) else value}\n')
    print(json.dumps(plan, indent=2))


def prepare(args):
    source_sha = args.source_sha or os.environ.get('GITHUB_SHA') or run('git', 'rev-parse', 'HEAD')
    source_marker(source_sha)
    if args.notes_only:
        path = REPO / 'artifacts/release-notes.md'
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(release_notes(project_version(), (REPO / 'CHANGELOG.md').read_text(encoding='utf8'),
                        (REPO / 'docs/release-notes.md').read_text(encoding='utf8'), source_sha), encoding='utf8')
        return
    if args.mode == 'artifacts':
        write_outputs({'version': project_version(), 'draft': True, 'action': 'artifacts'}, source_sha)
        return
    if not args.dry_run:
        if os.environ.get('GITHUB_REF') != 'refs/heads/main':
            raise ValueError('Publishing is allowed only from main. Use artifacts mode on other branches.')
        run('git', 'fetch', 'origin', 'main', '--tags')
        main_sha = run('git', 'rev-parse', 'origin/main')
        if main_sha != run('git', 'rev-parse', 'HEAD'):
            # Include newer commits, even if the newer push only changed docs.
            run('git', 'checkout', '--detach', main_sha)
        source_sha = source_commit(main_sha, run('git', 'log', '-1', '--format=%B', main_sha))
    repository = os.environ.get('GH_REPO') or run('gh', 'repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner')
    pages = json.loads(run('gh', 'api', f'repos/{repository}/releases?per_page=100', '--paginate', '--slurp'))
    releases = [release for page in pages for release in page]
    tags = run('git', 'tag', '--list').splitlines()
    declared = project_version()
    matching_tag = 'v' + declared
    reuse_tag = matching_tag in tags and run('git', 'rev-parse', matching_tag + '^{commit}') == run('git', 'rev-parse', 'HEAD')
    published_tags = [release['tag_name'] for release in releases if not release['draft']
                      and not release.get('prerelease') and stable_version(release['tag_name']) is not None]
    baseline_tag = max(published_tags, key=version_tuple, default=None)
    history = f'{baseline_tag}..{source_sha}' if baseline_tag in tags else source_sha
    message = run('git', 'log', '--format=%B', history)
    plan = plan_release(declared, releases, tags, source_sha,
                        force_draft=args.mode == 'draft' or '[release-review]' in message, reuse_tag=reuse_tag)
    if args.dry_run:
        write_outputs(plan, source_sha)
        return
    if plan['action'] == 'resume':
        run('git', 'checkout', '--detach', 'v' + plan['version'])
    elif plan['action'] == 'release' and plan['version'] != declared:
        record_version(plan['version'], source_sha)
        run('git', 'config', 'user.name', 'github-actions[bot]')
        run('git', 'config', 'user.email', '41898282+github-actions[bot]@users.noreply.github.com')
        run('git', 'add', 'Hwmon.csproj', 'CHANGELOG.md')
        run('git', 'commit', '-m', f'chore: set release version {plan["version"]}', '-m', f'hwmon-source:{source_sha}')
        # A normal fast-forward push fails safely if a human pushed newer code.
        run('git', 'push', 'origin', 'HEAD:main')
    write_outputs(plan, source_sha)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=['automatic', 'draft', 'artifacts'], default='automatic')
    parser.add_argument('--dry-run', action='store_true', help='Read release state and print the plan without changing Git.')
    parser.add_argument('--notes-only', action='store_true')
    parser.add_argument('--source-sha')
    try:
        prepare(parser.parse_args())
    except (ValueError, subprocess.CalledProcessError) as error:
        parser.exit(2, 'release: ' + str(error) + '\n')
