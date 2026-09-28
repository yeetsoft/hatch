import { describe, expect, it } from 'vitest';
import { buildTitle, buildWords, failedBuilds } from './buildCheck';
import type { BuildCheck } from '../types';

const check = (over: Partial<BuildCheck> = {}): BuildCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  branch: 'aer-12-thing',
  sha: '2'.repeat(40),
  shaSince: '2026-09-09T12:00:00Z',
  verdict: 'failed',
  failing: [
    { name: 'api', url: 'https://forge.example/checks/1' },
    { name: 'CI', url: null },
  ],
  pushedByIncrement: false,
  checkedAt: '2026-09-09T12:00:00Z',
  runner: 'box:/work/repo',
  checkedBy: 'runner',
  ...over,
});

describe('failedBuilds', () => {
  it('keeps a failed verdict', () => {
    expect(failedBuilds([check()])).toHaveLength(1);
  });

  /* A build that passed, is running or never ran shows nothing - and so does a
     board that has never heard of any of this. */
  it.each(['passed', 'pending', 'none'])('drops a %s verdict', (verdict) => {
    expect(failedBuilds([check({ verdict, failing: [] })])).toEqual([]);
  });

  it('draws nothing for no verdict at all', () => {
    expect(failedBuilds([])).toEqual([]);
    expect(failedBuilds(null)).toEqual([]);
    expect(failedBuilds(undefined)).toEqual([]);
  });

  it('drops the passing repository beside a failing one', () => {
    const kept = failedBuilds([
      check({ canonical: 'forge.example/owner/one', verdict: 'passed', failing: [] }),
      check({ canonical: 'forge.example/owner/two' }),
    ]);

    expect(kept.map((c) => c.canonical)).toEqual(['forge.example/owner/two']);
  });
});

describe('buildWords', () => {
  it('names the failing checks', () => {
    expect(buildWords(check())).toBe('Build failing: api, CI');
  });

  it('names the repository only when more than one fails', () => {
    expect(buildWords(check(), true)).toBe('Build failing: api, CI in forge.example/owner/repo');
  });
});

describe('buildTitle', () => {
  it('says which sha the build is about', () => {
    expect(buildTitle(check())).toBe('Build failing: api, CI\non 2222222222');
  });
});
