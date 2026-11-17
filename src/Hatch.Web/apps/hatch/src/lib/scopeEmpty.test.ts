import { describe, expect, it } from 'vitest';
import { scopeIsEmpty } from './scopeEmpty';
import type { Me, MeScope } from '../types';

const meOf = (scope?: MeScope): Me => ({
  kind: 'person',
  name: 'Ada',
  role: 'user',
  configured: true,
  canSignOut: true,
  canSignIn: false,
  scope,
});

describe('scopeIsEmpty', () => {
  it('is true for none', () => {
    expect(scopeIsEmpty(meOf({ mode: 'none', projectIds: [] }))).toBe(true);
  });

  it('is false for all and for some', () => {
    expect(scopeIsEmpty(meOf({ mode: 'all', projectIds: [] }))).toBe(false);
    expect(scopeIsEmpty(meOf({ mode: 'some', projectIds: [3, 7] }))).toBe(false);
  });

  it('is false with no answer, no scope, or a caller that is nobody', () => {
    expect(scopeIsEmpty(null)).toBe(false);
    expect(scopeIsEmpty(meOf())).toBe(false);
  });
});
