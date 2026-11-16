import { describe, expect, it } from 'vitest';
import { changelogSentence } from './changelogFiles';
import { fileForRepository } from './dependencyFiles';
import type { ChangelogFileDto } from '../types';

const file = (over: Partial<ChangelogFileDto> = {}): ChangelogFileDto => ({
  id: 1,
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '0123456789abcdef0123456789abcdef01234567',
  state: 'current',
  hash: 'deadbeef',
  behind: null,
  problem: null,
  url: null,
  checkedAt: '2026-11-16T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

describe('changelogSentence', () => {
  it('says current at the short sha', () => {
    expect(changelogSentence(file())).toBe('changelog current at 0123456');
  });

  it('says behind by several entries', () => {
    expect(changelogSentence(file({ state: 'behind', behind: 2 }))).toBe('changelog behind by 2 entries');
  });

  it('says behind by one entry, in the singular', () => {
    expect(changelogSentence(file({ state: 'behind', behind: 1 }))).toBe('changelog behind by 1 entry');
  });

  it('says no changelog on trunk yet where the file is absent', () => {
    expect(changelogSentence(file({ state: 'absent' }))).toBe('no changelog on trunk yet');
  });

  it('gives a foreign file the sentence the board stored, naming the sha and what to do', () => {
    const problem =
      '.hatch/changelog.yml at 0123456 is not a file Hatch wrote (it does not open with Hatch\'s header) - move or remove it before Hatch will write there';

    expect(changelogSentence(file({ state: 'foreign', problem }))).toBe(problem);
  });
});

describe('fileForRepository on changelog files', () => {
  it('picks the sole file for a canonical, and the one on the base branch where one is set', () => {
    const files = [file({ trunk: 'main' }), file({ id: 2, trunk: 'develop' })];

    expect(fileForRepository({ canonical: 'forge.example/owner/repo', baseBranch: 'develop' }, files)?.id).toBe(2);
    expect(fileForRepository({ canonical: 'forge.example/owner/repo', baseBranch: null }, files)).toBeNull();
    expect(fileForRepository({ canonical: 'forge.example/owner/repo', baseBranch: null }, [files[0]])?.id).toBe(1);
  });
});
