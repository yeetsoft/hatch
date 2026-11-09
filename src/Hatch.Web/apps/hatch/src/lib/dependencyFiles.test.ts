import { describe, expect, it } from 'vitest';
import { declarationSentence, fileForRepository, invalidSentence } from './dependencyFiles';
import type { DependencyFileDto } from '../types';

const file = (over: Partial<DependencyFileDto> = {}): DependencyFileDto => ({
  id: 1,
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  sha: '1'.repeat(40),
  state: 'parsed',
  hash: 'deadbeef',
  entries: [],
  problem: null,
  lastGoodSha: null,
  lastGoodHash: null,
  url: null,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

describe('declarationSentence', () => {
  it('joins each entry by comma, with its version where it has one', () => {
    const f = file({
      entries: [
        { id: 'dotnet', label: '.NET SDK', version: '10.0' },
        { id: 'node', label: 'Node', version: null },
        { id: 'make', label: 'make', version: '' },
      ],
    });

    expect(declarationSentence(f)).toBe('needs .NET SDK 10.0, Node, make');
  });

  it('says so where the declaration names nothing', () => {
    expect(declarationSentence(file({ entries: [] }))).toBe('declares no dependencies');
  });
});

describe('invalidSentence', () => {
  it('is the problem alone where there is no last good sha', () => {
    expect(invalidSentence(file({ state: 'invalid', problem: 'line 3: not a list' }))).toBe('line 3: not a list');
  });

  it('adds the short last good sha where there is one', () => {
    const f = file({ state: 'invalid', problem: 'line 3: not a list', lastGoodSha: 'abcdef0123456789' });

    expect(invalidSentence(f)).toBe('line 3: not a list — last good sha abcdef0');
  });
});

describe('fileForRepository', () => {
  const main = file({ id: 1, trunk: 'main' });
  const release = file({ id: 2, trunk: 'release' });
  const other = file({ id: 3, canonical: 'forge.example/owner/other' });
  const canonical = 'forge.example/owner/repo';

  it('picks the file read on the base branch where one is set', () => {
    expect(fileForRepository({ canonical, baseBranch: 'release' }, [main, release, other])).toBe(release);
  });

  it('is null where nothing was read on the base branch that is set', () => {
    expect(fileForRepository({ canonical, baseBranch: 'develop' }, [main, release])).toBeNull();
  });

  it('picks the sole file for the canonical where no base branch is set', () => {
    expect(fileForRepository({ canonical, baseBranch: null }, [main, other])).toBe(main);
  });

  it('is null where no base branch is set and two trunks were read', () => {
    expect(fileForRepository({ canonical, baseBranch: null }, [main, release])).toBeNull();
  });

  it('is null where no file matches the canonical at all', () => {
    expect(fileForRepository({ canonical, baseBranch: null }, [other])).toBeNull();
    expect(fileForRepository({ canonical, baseBranch: null }, [])).toBeNull();
  });
});
