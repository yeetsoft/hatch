import { describe, expect, it } from 'vitest';
import { DEVELOPMENT_REVISION, buildTimePhrase, earnsRevisionNotice, isStamped, shortSha } from './revision';
import type { ClientRevisionVerdict } from '../types';

const NOW = new Date('2026-09-07T08:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const SHA = 'a1b2c3d4e5f60718293a4b5c6d7e8f9011121314';

describe('isStamped', () => {
  it('is true for a full sha', () => {
    expect(isStamped(SHA)).toBe(true);
  });

  it('is false for an unstamped build', () => {
    expect(isStamped(DEVELOPMENT_REVISION)).toBe(false);
  });
});

describe('shortSha', () => {
  it('keeps seven characters out of a full sha, and keeps the full one too', () => {
    expect(shortSha(SHA)).toBe(SHA.slice(0, 7));
    expect(shortSha(SHA)).toHaveLength(7);
    expect(SHA).toHaveLength(40);
  });

  it('does not slice an unstamped build', () => {
    expect(shortSha(DEVELOPMENT_REVISION)).toBe(DEVELOPMENT_REVISION);
  });
});

describe('buildTimePhrase', () => {
  it('says nothing for an unstamped build, even with a builtAt', () => {
    expect(buildTimePhrase(DEVELOPMENT_REVISION, at(-3 * 60 * 60_000), NOW)).toBeNull();
  });

  it('says nothing for a stamped build with no builtAt', () => {
    expect(buildTimePhrase(SHA, null, NOW)).toBeNull();
  });

  it('gives the relative half exactly, and a non-empty absolute half', () => {
    const phrase = buildTimePhrase(SHA, at(-3 * 60 * 60_000), NOW);

    expect(phrase).not.toBeNull();
    expect(phrase?.relative).toBe('3 hours ago');
    expect(phrase?.absolute.length).toBeGreaterThan(0);
  });
});

describe('earnsRevisionNotice', () => {
  const verdict = (drift: ClientRevisionVerdict['drift']): ClientRevisionVerdict => ({
    revision: SHA,
    sequence: 42,
    drift,
  });

  it('is false when there is no client verdict at all', () => {
    expect(earnsRevisionNotice(null)).toBe(false);
  });

  it('is true only for Behind', () => {
    expect(earnsRevisionNotice(verdict('Behind'))).toBe(true);
  });

  it('is false for Unknown, Current and Ahead', () => {
    expect(earnsRevisionNotice(verdict('Unknown'))).toBe(false);
    expect(earnsRevisionNotice(verdict('Current'))).toBe(false);
    expect(earnsRevisionNotice(verdict('Ahead'))).toBe(false);
  });
});
