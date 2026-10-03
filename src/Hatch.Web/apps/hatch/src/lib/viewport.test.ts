import { describe, expect, it } from 'vitest';
import { isStandalone, matchesPhone, matchesStandalone, PHONE_QUERY } from './viewport';

describe('PHONE_QUERY', () => {
  it('is the one breakpoint, written the same way everywhere it appears', () => {
    expect(PHONE_QUERY).toBe('(max-width: 40rem)');
  });
});

describe('matchesPhone', () => {
  /* This workspace's tests run in plain Node (no jsdom) - window itself is
     absent, not just matchMedia. usePhone() needs a DOM to exercise beyond
     this, so it's the browser pass's, not a unit test's. */
  it('answers false where window is absent', () => {
    expect(matchesPhone()).toBe(false);
  });
});

describe('isStandalone', () => {
  it('is true when both signals say so', () => {
    expect(isStandalone(true, true)).toBe(true);
  });

  it('is false when neither signal says so', () => {
    expect(isStandalone(false, false)).toBe(false);
  });

  it('is true from the display-mode match alone', () => {
    expect(isStandalone(true, false)).toBe(true);
  });

  it('is true from the iOS flag alone', () => {
    expect(isStandalone(false, true)).toBe(true);
  });
});

describe('matchesStandalone', () => {
  /* Same reasoning as matchesPhone's own test: window itself is absent in
     this workspace's tests, so the guard chain has to short-circuit before
     ever reaching window.navigator. */
  it('answers false where window is absent', () => {
    expect(matchesStandalone()).toBe(false);
  });
});
