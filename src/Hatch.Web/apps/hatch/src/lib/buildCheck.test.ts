import { describe, expect, it } from 'vitest';
import { buildTitle, buildWords, checkLink, failedBuilds } from './buildCheck';
import type { BuildCheck } from '../types';

const check = (over: Partial<BuildCheck> = {}): BuildCheck => ({
  remote: 'git@forge.example:owner/repo.git',
  canonical: 'forge.example/owner/repo',
  branch: 'aer-12-thing',
  sha: '2'.repeat(40),
  verdict: 'failed',
  failing: [
    { name: 'build', url: 'https://ci.example/1' },
    { name: 'test', url: null },
  ],
  runner: 'box:/work/repo',
  pushedByIncrement: false,
  shaSince: '2026-09-09T12:00:00Z',
  checkedAt: '2026-09-09T12:00:00Z',
  ...over,
});

describe('failedBuilds', () => {
  it('keeps a failed verdict', () => {
    expect(failedBuilds([check()])).toHaveLength(1);
  });

  /* A passing build, one still running, or none shows nothing - and so does a
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
  it('names the checks that failed', () => {
    expect(buildWords(check())).toBe('Build fails: build, test');
  });

  it('says which repository only when more than one fails', () => {
    expect(buildWords(check(), true)).toBe('Build fails: build, test in forge.example/owner/repo');
  });
});

describe('buildTitle', () => {
  it('names every check, one to a line, under the sentence', () => {
    expect(buildTitle(check())).toBe('Build fails: build, test\nbuild\ntest');
  });
});

describe('checkLink', () => {
  it('links an http or https address', () => {
    expect(checkLink({ name: 'a', url: 'https://ci.example/1' })).toBe('https://ci.example/1');
    expect(checkLink({ name: 'a', url: 'http://ci.example/1' })).toBe('http://ci.example/1');
  });

  it('links nothing without an address', () => {
    expect(checkLink({ name: 'a', url: null })).toBeNull();
  });

  /* A key writes these, and the page turns them into anchors. */
  it.each(['javascript:alert(1)', 'data:text/html,x', '/relative', 'not a url'])('refuses %s', (url) => {
    expect(checkLink({ name: 'a', url })).toBeNull();
  });
});
