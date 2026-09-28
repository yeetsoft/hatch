import { describe, expect, it } from 'vitest';
import type { MergeCheck } from '../types';
import { conflictFileWords, conflictOf, conflictWords, mergeCheckEventWords } from './mergeCheck';

function check(over: Partial<MergeCheck>): MergeCheck {
  return {
    remote: 'git@forge.example:acme/hatch.git',
    canonical: 'forge.example/acme/hatch',
    trunk: 'main',
    trunkSha: 'a'.repeat(40),
    verdict: 'clean',
    branch: 'ha-1-thing',
    branchSha: 'b'.repeat(40),
    files: [],
    checkedAt: '2026-09-27T12:00:00Z',
    runner: 'runner-1',
    checkedBy: 'Nathan',
    ...over,
  };
}

describe('conflictOf', () => {
  it('is nothing for an issue no runner has checked', () => {
    expect(conflictOf(undefined)).toBeNull();
    expect(conflictOf([])).toBeNull();
  });

  it.each(['clean', 'none', 'ambiguous'] as const)('is nothing for a %s verdict', (verdict) => {
    expect(conflictOf([check({ verdict })])).toBeNull();
  });

  it('names the trunk and the files of a conflicted verdict', () => {
    expect(conflictOf([check({ verdict: 'conflicted', files: ['a.txt', 'b.txt'] })])).toEqual({
      trunk: 'main',
      files: ['a.txt', 'b.txt'],
    });
  });

  /* An issue conflicts if any of its verdicts does, and the page names what
     conflicts - not what merely was checked. */
  it('takes the conflicted repository and not the clean one beside it', () => {
    const conflict = conflictOf([
      check({ verdict: 'clean' }),
      check({ canonical: 'forge.example/acme/docs', verdict: 'conflicted', trunk: 'trunk', files: ['x'] }),
    ]);

    expect(conflict).toEqual({ trunk: 'trunk', files: ['x'] });
  });

  it('lists a file once when two repositories name it', () => {
    const conflict = conflictOf([
      check({ verdict: 'conflicted', files: ['a.txt'] }),
      check({ canonical: 'forge.example/acme/docs', verdict: 'conflicted', files: ['a.txt', 'b.txt'] }),
    ]);

    expect(conflict?.files).toEqual(['a.txt', 'b.txt']);
  });
});

describe('conflictWords', () => {
  it('counts the files', () => {
    expect(conflictWords({ trunk: 'main', files: ['a', 'b', 'c'] })).toBe('Conflicts with main: 3 files');
  });

  it('says one file in the singular', () => {
    expect(conflictWords({ trunk: 'main', files: ['a'] })).toBe('Conflicts with main: 1 file');
  });
});

describe('conflictFileWords', () => {
  it('lists what fits', () => {
    expect(conflictFileWords(['a', 'b'])).toBe('a, b');
  });

  it('says how many more when it cuts', () => {
    expect(conflictFileWords(['a', 'b', 'c', 'd'], 2)).toBe('a, b and 2 more');
  });
});

describe('mergeCheckEventWords', () => {
  it('reads a first verdict as coming from nothing', () => {
    expect(mergeCheckEventWords({ from: null, to: { verdict: 'clean', files: [] } })).toBe('none → clean');
  });

  it('names the files a conflict arrived with', () => {
    expect(
      mergeCheckEventWords({
        from: { verdict: 'clean', files: [] },
        to: { verdict: 'conflicted', files: ['a.txt'] },
      }),
    ).toBe('clean → conflicted (a.txt)');
  });
});
