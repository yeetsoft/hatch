import { describe, expect, it } from 'vitest';
import { conflictTitle, conflictWords, conflictedChecks } from './mergeCheck';
import type { MergeCheck } from '../types';

const check = (over: Partial<MergeCheck> = {}): MergeCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  trunk: 'main',
  trunkSha: '1'.repeat(40),
  verdict: 'conflicted',
  branch: 'aer-12-thing',
  branchSha: '2'.repeat(40),
  files: ['a.cs', 'b.cs', 'c.cs'],
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

describe('conflictedChecks', () => {
  it('keeps a conflicted verdict', () => {
    expect(conflictedChecks([check()])).toHaveLength(1);
  });

  /* A clean verdict, no branch, or no verdict shows nothing - and so does a
     board that has never heard of any of this. */
  it.each(['clean', 'none', 'ambiguous'])('drops a %s verdict', (verdict) => {
    expect(conflictedChecks([check({ verdict, files: [] })])).toEqual([]);
  });

  it('draws nothing for no verdict at all', () => {
    expect(conflictedChecks([])).toEqual([]);
    expect(conflictedChecks(null)).toEqual([]);
    expect(conflictedChecks(undefined)).toEqual([]);
  });

  it('drops the clean repository beside a conflicted one', () => {
    const kept = conflictedChecks([
      check({ canonical: 'forge.example/owner/one', verdict: 'clean', files: [] }),
      check({ canonical: 'forge.example/owner/two' }),
    ]);

    expect(kept.map((c) => c.canonical)).toEqual(['forge.example/owner/two']);
  });
});

describe('conflictWords', () => {
  it('names the trunk and counts the files', () => {
    expect(conflictWords(check())).toBe('Conflicts with main: 3 files');
  });

  it('says 1 file and not 1 files', () => {
    expect(conflictWords(check({ files: ['a.cs'] }))).toBe('Conflicts with main: 1 file');
  });

  /* Nothing reads the trunk for a name it recognises. */
  it('leaves a trunk nobody has heard of exactly as it was reported', () => {
    expect(conflictWords(check({ trunk: 'release/2026.09' }))).toBe('Conflicts with release/2026.09: 3 files');
  });

  it('says which repository only when more than one conflicts', () => {
    expect(conflictWords(check(), true)).toBe('Conflicts with main: 3 files in forge.example/owner/repo');
  });
});

describe('conflictTitle', () => {
  it('names every file, one to a line, under the sentence', () => {
    expect(conflictTitle(check())).toBe('Conflicts with main: 3 files\na.cs\nb.cs\nc.cs');
  });
});
