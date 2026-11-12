import { describe, expect, it } from 'vitest';
import { DEFAULT_LANDING, safeReturnTo, signInHref } from './returnTo';

describe('safeReturnTo', () => {
  it('keeps rooted same-origin paths', () => {
    expect(safeReturnTo('/plan')).toBe('/plan');
    expect(safeReturnTo('/x?a=b')).toBe('/x?a=b');
    expect(safeReturnTo('/')).toBe('/');
  });

  it('drops everything else', () => {
    for (const raw of [null, '', 'https://x', '//x', '/\\x', 'x']) {
      expect(safeReturnTo(raw)).toBeNull();
    }
  });
});

describe('DEFAULT_LANDING', () => {
  it('is the origin root, where the board is served', () => {
    expect(DEFAULT_LANDING).toBe('/');
  });

  it('is itself a safe return path', () => {
    expect(safeReturnTo(DEFAULT_LANDING)).toBe(DEFAULT_LANDING);
  });
});

describe('signInHref', () => {
  it('carries a safe r, encoded', () => {
    expect(signInHref('/plan')).toBe('/api/auth/google/start?r=%2Fplan');
  });

  it('drops an unsafe r', () => {
    for (const raw of [null, '', 'https://x', '//x', '/\\x']) {
      expect(signInHref(raw)).toBe('/api/auth/google/start');
    }
  });
});
