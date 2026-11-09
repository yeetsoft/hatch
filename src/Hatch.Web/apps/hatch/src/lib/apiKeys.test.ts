import { describe, expect, it } from 'vitest';
import {
  dismissSecret,
  lastUsedLabel,
  mintedAsKey,
  mintProblem,
  ownerLabel,
  partitionKeys,
  runnerAsKey,
  runnersAsKeys,
  showSecret,
} from './apiKeys';
import type { ApiKey, Runner } from '../types';

const key = (over: Partial<ApiKey> = {}): ApiKey => ({
  id: 'k1',
  name: 'Claude',
  prefix: 'hatch_ak_ab',
  createdAt: '2026-09-08T12:00:00Z',
  lastUsedAt: null,
  revokedAt: null,
  owner: null,
  ...over,
});

const runner = (over: Partial<Runner> = {}): Runner =>
  ({
    name: 'Claude',
    firstSeenAt: '2026-09-07T00:00:00Z',
    apiKeyId: 'k1',
    prefix: 'hatch_ak_ab',
    createdAt: '2026-09-08T12:00:00Z',
    lastUsedAt: null,
    revokedAt: null,
    ownerPersonId: null,
    for: null,
    ...over,
  }) as Runner;

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

describe('runnerAsKey', () => {
  it('maps a keyed row field for field, owner included', () => {
    const r = runner({ ownerPersonId: 'p1', for: 'Ada', revokedAt: '2026-09-09T00:00:00Z' });
    expect(runnerAsKey(r)).toEqual({
      id: 'k1',
      name: 'Claude',
      prefix: 'hatch_ak_ab',
      createdAt: '2026-09-08T12:00:00Z',
      lastUsedAt: null,
      revokedAt: '2026-09-09T00:00:00Z',
      owner: { id: 'p1', name: 'Ada' },
    });
  });

  it('is null for a heartbeat no key claims', () => {
    expect(runnerAsKey(runner({ apiKeyId: null }))).toBeNull();
  });

  it('treats an owner id with no name as unowned', () => {
    expect(runnerAsKey(runner({ ownerPersonId: 'p1', for: null }))?.owner).toBeNull();
  });

  it('falls back to firstSeenAt when createdAt is missing', () => {
    expect(runnerAsKey(runner({ createdAt: null }))?.createdAt).toBe('2026-09-07T00:00:00Z');
  });
});

describe('runnersAsKeys', () => {
  it('drops heartbeats with no key and keeps the rest in order', () => {
    const a = runner({ name: 'a', apiKeyId: 'a' });
    const b = runner({ name: 'b', apiKeyId: null });
    const c = runner({ name: 'c', apiKeyId: 'c' });
    expect(runnersAsKeys([a, b, c]).map((k) => k.name)).toEqual(['a', 'c']);
  });
});

describe('mintedAsKey', () => {
  it('carries the secret and the mapped key', () => {
    expect(mintedAsKey({ runner: runner(), secret: 'hatch_ak_thesecret' })).toEqual({
      key: runnerAsKey(runner()),
      secret: 'hatch_ak_thesecret',
    });
  });
});

describe('labels', () => {
  it('says never for a key that was never used', () => {
    expect(lastUsedLabel(null)).toBe('never');
    expect(lastUsedLabel('2026-09-08T12:00:00Z')).not.toBe('never');
  });

  it('says nobody for an unowned key', () => {
    expect(ownerLabel(null)).toBe('nobody');
    expect(ownerLabel({ id: 'p1', name: 'Ada' })).toBe('Ada');
  });
});
