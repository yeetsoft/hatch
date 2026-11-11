import { describe, expect, it } from 'vitest';
import { originChain, originHref, originLabel, originParts } from './contribution';
import type { ContributionRecord, IssueCard } from '../types';

const record = (over: Partial<ContributionRecord> = {}): ContributionRecord => ({
  pullRequestUrl: null,
  headBranch: null,
  authorLogin: 'someone',
  authorAssociation: 'NONE',
  filedAt: '2026-11-11T00:00:00Z',
  filedBy: 'runner',
  ...over,
});

describe('originLabel', () => {
  it('is the key and the instance host where the record has both', () => {
    const c = record({ originKey: 'AER-12', originInstance: 'https://hatch.example.org' });
    expect(originLabel(c)).toBe('AER-12 · hatch.example.org');
    expect(originParts(c)).toEqual({ name: 'AER-12', host: 'hatch.example.org' });
  });

  it('is the key alone where there is no instance', () => {
    expect(originLabel(record({ originKey: 'AER-12' }))).toBe('AER-12');
  });

  it('is the raw instance where it does not parse as an address', () => {
    expect(originLabel(record({ originKey: 'AER-12', originInstance: 'not a url' }))).toBe('AER-12 · not a url');
  });

  it('is @login where the record has no origin key', () => {
    expect(originLabel(record())).toBe('@someone');
  });
});

describe('originHref', () => {
  it('links an http(s) instance', () => {
    expect(originHref(record({ originInstance: 'https://hatch.example.org' }))).toBe('https://hatch.example.org/');
    expect(originHref(record({ originInstance: 'http://hatch.example.org' }))).toBe('http://hatch.example.org/');
  });

  it('links nothing else', () => {
    expect(originHref(record())).toBeNull();
    expect(originHref(record({ originInstance: 'javascript:alert(1)' }))).toBeNull();
    expect(originHref(record({ originInstance: 'ftp://hatch.example.org' }))).toBeNull();
    expect(originHref(record({ originInstance: 'garbage' }))).toBeNull();
  });
});

describe('originChain', () => {
  const card = (key: string, parentKey: string | null, contribution: ContributionRecord | null): IssueCard =>
    ({ key, parentKey, contribution, type: 'task', title: key }) as IssueCard;

  it('lists the ancestors with a record, root first', () => {
    const cards = [
      card('A-3', 'A-2', null),
      card('A-2', 'A-1', record({ originKey: 'X-2' })),
      card('A-1', null, record({ originKey: 'X-1' })),
    ];
    expect(originChain('A-2', cards).map((c) => c.key)).toEqual(['A-1', 'A-2']);
  });

  it('passes over an ancestor with no record and keeps walking', () => {
    const cards = [card('A-2', 'A-1', null), card('A-1', null, record())];
    expect(originChain('A-2', cards).map((c) => c.key)).toEqual(['A-1']);
  });

  it('stops at an ancestor the board does not carry', () => {
    const cards = [card('A-2', 'A-1', record())];
    expect(originChain('A-2', cards).map((c) => c.key)).toEqual(['A-2']);
  });

  it('is empty with no parent, or no record anywhere above', () => {
    expect(originChain(null, [])).toEqual([]);
    expect(originChain('A-1', [card('A-1', null, null)])).toEqual([]);
  });

  it('ends on a cycle', () => {
    const cards = [card('A-1', 'A-2', record()), card('A-2', 'A-1', record())];
    expect(originChain('A-1', cards).map((c) => c.key)).toEqual(['A-2', 'A-1']);
  });
});
