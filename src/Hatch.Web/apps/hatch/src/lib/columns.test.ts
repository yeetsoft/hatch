import { describe, expect, it } from 'vitest';
import { isObviated, landingFocus, statusChoices } from './columns';
import type { Status } from '../types';

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  color: '#6b7280',
  ...opts,
});

const INBOX = status(1, 0);
const TODO = status(2, 1);
const DONE = status(3, 2, { isTerminal: true });
const SHELVED = status(4, 3, { isDeferred: true });
const PARKED = status(5, 4, { isDeferred: true });

describe('statusChoices', () => {
  it('splits the board from the shelf, in the order given', () => {
    expect(statusChoices([INBOX, TODO, DONE, SHELVED, PARKED])).toEqual({
      lanes: [INBOX, TODO, DONE],
      shelf: [SHELVED, PARKED],
    });
  });

  it('has no shelf on a board with no deferred column', () => {
    expect(statusChoices([INBOX, TODO, DONE])).toEqual({ lanes: [INBOX, TODO, DONE], shelf: [] });
  });
});

describe('landingFocus', () => {
  const statuses = [INBOX, TODO, DONE, SHELVED];

  it('lands on the lane after the current one', () => {
    expect(landingFocus(statuses, INBOX.id)).toBe(TODO.id);
  });

  it('lands on the lane before the current one, from the last lane', () => {
    expect(landingFocus(statuses, DONE.id)).toBe(TODO.id);
  });

  it('lands on the first lane, from a deferred column', () => {
    expect(landingFocus(statuses, SHELVED.id)).toBe(INBOX.id);
  });

  it('lands on the first lane, from an id the board does not recognise', () => {
    expect(landingFocus(statuses, 404)).toBe(INBOX.id);
  });

  it('has nowhere to land on a board with one lane', () => {
    expect(landingFocus([INBOX], INBOX.id)).toBeNull();
  });
});

describe('isObviated', () => {
  it('is true in a terminal column', () => {
    expect(isObviated(DONE)).toBe(true);
  });

  it('is false in a deferred column', () => {
    expect(isObviated(SHELVED)).toBe(false);
  });

  it('is false in an ordinary column', () => {
    expect(isObviated(TODO)).toBe(false);
  });
});
