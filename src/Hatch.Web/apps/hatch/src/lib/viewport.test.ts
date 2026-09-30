import { describe, expect, it } from 'vitest';
import { matchesPhone, PHONE_QUERY } from './viewport';

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
