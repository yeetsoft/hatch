import { describe, expect, it } from 'vitest';
import {
  dismissSecret,
  lastUsedLabel,
  mintProblem,
  ownerLabel,
  partitionKeys,
  scopesLabel,
  showSecret,
  toggleScope,
} from './apiKeys';
import type { ApiKey } from '../types';

const key = (over: Partial<ApiKey> = {}): ApiKey => ({
  id: 'k1',
  name: 'Claude',
  prefix: 'hatch_ak_ab',
  scopes: ['hatch'],
  createdAt: '2026-09-08T12:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  owner: null,
  ...over,
});

describe('partitionKeys', () => {
  it('splits live from revoked and keeps the order', () => {
    const a = key({ id: 'a' });
    const b = key({ id: 'b', revokedAt: '2026-09-09T00:00:00Z' });
    const c = key({ id: 'c' });
    const { live, revoked } = partitionKeys([a, b, c]);
    expect(live.map((k) => k.id)).toEqual(['a', 'c']);
    expect(revoked.map((k) => k.id)).toEqual(['b']);
  });
});

describe('mintProblem', () => {
  it('refuses a blank name', () => {
    expect(mintProblem('   ')).not.toBeNull();
  });

  it('refuses a name over the limit', () => {
    expect(mintProblem('x'.repeat(121))).not.toBeNull();
    expect(mintProblem('x'.repeat(120))).toBeNull();
  });

  it('accepts an ordinary name', () => {
    expect(mintProblem('Claude')).toBeNull();
  });
});

describe('toggleScope', () => {
  it('adds and removes', () => {
    expect(toggleScope([], 'hatch')).toEqual(['hatch']);
    expect(toggleScope(['hatch'], 'hatch')).toEqual([]);
  });
});

describe('the secret panel', () => {
  const minted = (secret: string) => ({ key: key(), secret });

  it('keeps the secret while shown', () => {
    expect(showSecret(minted('hatch_ak_thesecret'))?.secret).toBe('hatch_ak_thesecret');
  });

  it('drops it when dismissed', () => {
    expect(dismissSecret()).toBeNull();
  });

  it('is replaced by a second mint, and the first is not kept', () => {
    const second = showSecret(minted('hatch_ak_another'));
    expect(second?.secret).toBe('hatch_ak_another');
    expect(JSON.stringify(second)).not.toContain('thesecret');
  });
});

describe('labels', () => {
  it('says none for no scopes', () => {
    expect(scopesLabel([])).toBe('none');
    expect(scopesLabel(['a', 'b'])).toBe('a, b');
  });

  it('says never for a key that was never used', () => {
    expect(lastUsedLabel(null)).toBe('never');
    expect(lastUsedLabel('2026-09-08T12:00:00Z')).not.toBe('never');
  });

  it('says nobody for an unowned key', () => {
    expect(ownerLabel(null)).toBe('nobody');
    expect(ownerLabel({ id: 'p1', name: 'Ada' })).toBe('Ada');
  });
});
