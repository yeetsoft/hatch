import { describe, expect, it } from 'vitest';
import { safeReturnTo, signInHref } from './returnTo';

describe('safeReturnTo', () => {
  it('keeps rooted same-origin paths', () => {
    expect(safeReturnTo('/apps/hatch/plan')).toBe('/apps/hatch/plan');
    expect(safeReturnTo('/x?a=b')).toBe('/x?a=b');
    expect(safeReturnTo('/')).toBe('/');
  });

  it('drops everything else', () => {
    for (const raw of [null, '', 'https://x', '//x', '/\\x', 'x']) {
      expect(safeReturnTo(raw)).toBeNull();
    }
  });
});

describe('signInHref', () => {
  it('carries a safe r, encoded', () => {
    expect(signInHref('/apps/hatch/plan')).toBe('/api/auth/google/start?r=%2Fapps%2Fhatch%2Fplan');
  });

  it('drops an unsafe r', () => {
    for (const raw of [null, '', 'https://x', '//x', '/\\x']) {
      expect(signInHref(raw)).toBe('/api/auth/google/start');
    }
  });
});
