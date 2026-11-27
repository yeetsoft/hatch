import { describe, expect, it } from 'vitest';
import { holdFirstRead, shouldRecord, shownBasis, sinceToSend } from './ambitionVisit';
import type { AmbitionChanges } from '../types';

const read = (over: Partial<AmbitionChanges> = {}): AmbitionChanges => ({
  since: '2026-11-13T12:00:00Z',
  basis: 'week',
  omitted: 0,
  lines: [],
  ...over,
});

describe('sinceToSend', () => {
  it('sends no since before a visit is held', () => {
    expect(sinceToSend(null)).toBeUndefined();
  });

  it("sends the held read's since after", () => {
    expect(sinceToSend(holdFirstRead(null, read()))).toBe('2026-11-13T12:00:00Z');
  });
});

describe('holdFirstRead', () => {
  it('keeps the first read, and no later one', () => {
    const held = holdFirstRead(null, read());

    expect(holdFirstRead(held, read({ since: '2026-11-20T12:00:00Z', basis: 'given' }))).toEqual({
      since: '2026-11-13T12:00:00Z',
      basis: 'week',
    });
  });
});

describe('shouldRecord', () => {
  const drawn = { current: true, changes: read(), recorded: false };

  it('records a draw made for the scope in force', () => {
    expect(shouldRecord(drawn)).toBe(true);
  });

  it('records nothing while the data is not for the scope in force', () => {
    expect(shouldRecord({ ...drawn, current: false })).toBe(false);
  });

  it('records nothing where what changed could not be read', () => {
    expect(shouldRecord({ ...drawn, changes: null })).toBe(false);
  });

  it('records only once in a mount', () => {
    expect(shouldRecord({ ...drawn, recorded: true })).toBe(false);
  });
});

describe('shownBasis', () => {
  it("draws the read's own basis before a visit is held", () => {
    expect(shownBasis(null, read({ basis: 'visit' }))).toBe('visit');
  });

  it('keeps the held basis over a refetch, which comes back given', () => {
    const held = holdFirstRead(null, read());

    expect(shownBasis(held, read({ basis: 'given' }))).toBe('week');
  });
});
