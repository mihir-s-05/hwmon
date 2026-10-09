"""Offline checks for automatic publishing and the review boundary."""
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from prepare_release import plan_release, release_notes, source_marker, source_commit
import prepare_release

SHA = 'a' * 40


def release(version, draft=False, body=''):
    return {'tag_name': 'v' + version, 'draft': draft, 'prerelease': False, 'body': body}


class ReleasePolicyTests(unittest.TestCase):
    def test_latest_human_commit_is_used_even_if_it_only_changes_docs(self):
        self.assertEqual(source_commit(SHA, 'Update WSL docs'), SHA)

    def test_version_bot_commit_recovers_original_source_for_retries(self):
        original = 'b' * 40
        self.assertEqual(source_commit(SHA, 'chore: set release version 0.2.2\n\nhwmon-source:' + original), original)

    def test_explicit_patch_is_published(self):
        plan = plan_release('0.2.1', [release('0.2.0')], ['v0.2.0'], SHA)
        self.assertEqual(plan, {'version': '0.2.1', 'draft': False, 'action': 'release'})

    def test_unchanged_version_gets_next_patch(self):
        plan = plan_release('0.2.1', [release('0.2.1')], ['v0.2.1'], SHA)
        self.assertEqual((plan['version'], plan['draft']), ('0.2.2', False))

    def test_patch_increments_numerically(self):
        plan = plan_release('0.2.9', [release('0.2.9')], ['v0.2.10'], SHA)
        self.assertEqual(plan['version'], '0.2.11')

    def test_minor_and_major_stay_drafts(self):
        for version in ['0.3.0', '1.0.0']:
            with self.subTest(version=version):
                self.assertTrue(plan_release(version, [release('0.2.1')], ['v0.2.1'], SHA)['draft'])

    def test_patch_in_unapproved_minor_stays_draft(self):
        plan = plan_release('0.3.0', [release('0.2.1'), release('0.3.0', draft=True)], ['v0.3.0'], SHA)
        self.assertEqual((plan['version'], plan['draft']), ('0.3.1', True))

    def test_patches_publish_after_minor_is_approved(self):
        plan = plan_release('0.3.0', [release('0.2.1'), release('0.3.0')], ['v0.3.0'], SHA)
        self.assertEqual((plan['version'], plan['draft']), ('0.3.1', False))

    def test_patch_can_be_marked_for_review(self):
        self.assertTrue(plan_release('0.2.1', [release('0.2.0')], [], SHA, force_draft=True)['draft'])

    def test_first_release_requires_review(self):
        self.assertTrue(plan_release('0.1.0', [], [], SHA)['draft'])

    def test_completed_push_is_not_released_twice(self):
        plan = plan_release('0.2.1', [release('0.2.2', body=source_marker(SHA))], [], SHA)
        self.assertEqual((plan['version'], plan['action']), ('0.2.2', 'skip'))

    def test_partial_patch_upload_can_resume_and_publish(self):
        plan = plan_release('0.2.1', [release('0.2.1'), release('0.2.2', draft=True, body=source_marker(SHA))], [], SHA)
        self.assertEqual(plan, {'version': '0.2.2', 'draft': False, 'action': 'resume'})

    def test_existing_tag_for_same_build_is_reused(self):
        plan = plan_release('0.2.2', [release('0.2.1')], ['v0.2.2'], SHA, reuse_tag=True)
        self.assertEqual((plan['version'], plan['draft']), ('0.2.2', False))

    def test_resumed_minor_release_still_requires_review(self):
        plan = plan_release('0.3.0', [release('0.2.1'), release('0.3.0', draft=True, body=source_marker(SHA))], [], SHA)
        self.assertEqual(plan, {'version': '0.3.0', 'draft': True, 'action': 'resume'})

    def test_old_minor_line_is_rejected(self):
        with self.assertRaises(ValueError):
            plan_release('0.2.1', [release('0.3.0')], ['v0.3.0'], SHA)

    def test_prerelease_is_not_published_automatically(self):
        with self.assertRaises(ValueError):
            plan_release('0.3.0-beta.1', [release('0.2.1')], [], SHA)

    def test_invalid_source_commit_is_rejected(self):
        with self.assertRaises(ValueError):
            plan_release('0.2.1', [], [], 'bad-sha')

    def test_notes_use_current_changelog_section(self):
        notes = release_notes('0.2.1', '# Changelog\n\n## 0.2.1\n\n- CSV fix.\n\n## 0.2.0\n\n- Old change.\n', 'Download guide.', SHA)
        self.assertIn('CSV fix.', notes)
        self.assertNotIn('Old change.', notes)
        self.assertIn(source_marker(SHA), notes)

    def test_auto_version_commit_updates_project_and_changelog(self):
        with tempfile.TemporaryDirectory(prefix='hwmon-release-test-') as directory:
            root = pathlib.Path(directory)
            project = root / 'Hwmon.csproj'
            changelog = root / 'CHANGELOG.md'
            project.write_text('<Project><PropertyGroup><Version>0.2.1</Version></PropertyGroup></Project>', encoding='utf8')
            changelog.write_text('# Changelog\n\n## 0.2.1\n\n- Previous fix.\n', encoding='utf8')
            with patch.object(prepare_release, 'REPO', root):
                prepare_release.record_version('0.2.2', SHA)
            self.assertIn('<Version>0.2.2</Version>', project.read_text())
            notes = changelog.read_text()
            self.assertLess(notes.index('## 0.2.2'), notes.index('## 0.2.1'))
            self.assertIn('Previous fix.', notes)

    def test_unreleased_notes_are_promoted_to_new_version(self):
        with tempfile.TemporaryDirectory(prefix='hwmon-release-test-') as directory:
            root = pathlib.Path(directory)
            (root / 'Hwmon.csproj').write_text('<Project><PropertyGroup><Version>0.2.1</Version></PropertyGroup></Project>', encoding='utf8')
            changelog = root / 'CHANGELOG.md'
            changelog.write_text('# Changelog\n\n## Unreleased\n\n- New GPU fix.\n\n## 0.2.1\n\n- Old fix.\n', encoding='utf8')
            with patch.object(prepare_release, 'REPO', root):
                prepare_release.record_version('0.2.2', SHA)
            self.assertIn('## 0.2.2\n\n- New GPU fix.', changelog.read_text())
            self.assertNotIn('Unreleased', changelog.read_text())


if __name__ == '__main__':
    unittest.main()
