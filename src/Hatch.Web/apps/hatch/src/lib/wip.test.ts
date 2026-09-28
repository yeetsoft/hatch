import { describe, expect, it } from 'vitest';
import { limitDraft, limitRequest, toggled, wipBlocked } from './wip';
import type { Status, WipSection } from '../types';

const status = (id: number, over: Partial<Status> = {}): Status => ({
  id,
  name: `Column ${id}`,
  sortOrder: id,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  color: '#336699',
  ...over,
});

const TODO = status(1);
const DOING = status(2);
const REVIEW = status(3);
const DONE = status(4, { isTerminal: true });
const SHELVED = status(5, { isDeferred: true });

const STATUSES = [TODO, DOING, REVIEW, DONE, SHELVED];

const section = (over: Partial<WipSection> = {}): WipSection => ({
  limit: null,
  types: ['story', 'bug'],
  statusIds: [],
  ...over,
});

describe('wipBlocked', () => {
  it('is null for an ordinary column', () => {
    expect(wipBlocked(TODO)).toBeNull();
  });

  it('names why a deferred column is blocked', () => {
    expect(wipBlocked(SHELVED)).toContain('parked work');
  });

  it('names why a done column is blocked', () => {
    expect(wipBlocked(DONE)).toContain('shipped work');
  });
});

describe('toggled', () => {
  it('adds a column, in board order', () => {
    const held = section({ statusIds: [DOING.id] });

    expect(toggled(held, STATUSES, REVIEW.id, true)).toEqual([DOING.id, REVIEW.id]);
  });

  it('adds a column ahead of ones already held, in board order rather than append order', () => {
    const held = section({ statusIds: [REVIEW.id] });

    expect(toggled(held, STATUSES, DOING.id, true)).toEqual([DOING.id, REVIEW.id]);
  });

  it('removes a column', () => {
    const held = section({ statusIds: [DOING.id, REVIEW.id] });

    expect(toggled(held, STATUSES, DOING.id, false)).toEqual([REVIEW.id]);
  });

  it('unticking a column not held changes nothing', () => {
    const held = section({ statusIds: [DOING.id] });

    expect(toggled(held, STATUSES, REVIEW.id, false)).toEqual([DOING.id]);
  });
});

describe('the limit field', () => {
  it('shows no limit as a blank box', () => {
    expect(limitDraft(section({ limit: null }))).toBe('');
  });

  it('shows a held limit as its number', () => {
    expect(limitDraft(section({ limit: 5 }))).toBe('5');
  });

  it('trims the field for the wire', () => {
    expect(limitRequest(' 5 ')).toBe('5');
  });

  it('a blank field clears the limit', () => {
    expect(limitRequest('  ')).toBe('');
  });
});
