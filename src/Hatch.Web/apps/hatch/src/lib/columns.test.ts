import { describe, expect, it } from 'vitest';
import { awaitingReview, implementation, isObviated, landingFocus, sendBack, statusChoices } from './columns';
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
  isProtected: false,
  mergesPullRequest: false,
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

describe('awaitingReview and implementation', () => {
  it('on a plain board, name review and doing', () => {
    const todo = status(1, 10);
    const doing = status(2, 20);
    const review = status(3, 30);
    const done = status(4, 40, { isTerminal: true });
    const board = [todo, doing, review, done];

    expect(awaitingReview(board)?.id).toBe(review.id);
    expect(implementation(board)?.id).toBe(doing.id);
  });

  it('step back over one protected column before done', () => {
    const todo = status(1, 10);
    const doing = status(2, 20);
    const review = status(3, 30);
    const approved = status(4, 40, { isProtected: true });
    const done = status(5, 50, { isTerminal: true });
    const board = [todo, doing, review, approved, done];

    expect(awaitingReview(board)?.id).toBe(review.id);
    expect(implementation(board)?.id).toBe(doing.id);
  });

  it('step back over two protected columns before done', () => {
    const todo = status(1, 10);
    const doing = status(2, 20);
    const review = status(3, 30);
    const approved = status(4, 40, { isProtected: true });
    const qa = status(5, 50, { isProtected: true });
    const done = status(6, 60, { isTerminal: true });
    const board = [todo, doing, review, approved, qa, done];

    expect(awaitingReview(board)?.id).toBe(review.id);
    expect(implementation(board)?.id).toBe(doing.id);
  });

  it('a protected column mid-board moves nothing', () => {
    const todo = status(1, 10);
    const doing = status(2, 20, { isProtected: true });
    const review = status(3, 30);
    const done = status(4, 40, { isTerminal: true });
    const board = [todo, doing, review, done];

    expect(awaitingReview(board)?.id).toBe(review.id);
    expect(implementation(board)?.id).toBe(doing.id);
  });

  it('a ticked column wins over the computed measurement', () => {
    const todo = status(1, 10, { isImplementation: true });
    const doing = status(2, 20);
    const review = status(3, 30);
    const done = status(4, 40, { isTerminal: true });
    const board = [todo, doing, review, done];

    expect(implementation(board)?.id).toBe(todo.id);
    expect(awaitingReview(board)?.id).toBe(review.id);
  });
});

describe('sendBack', () => {
  it('on a plain board, is the column before doing', () => {
    const todo = status(1, 10);
    const doing = status(2, 20);
    const review = status(3, 30);
    const done = status(4, 40, { isTerminal: true });
    const board = [todo, doing, review, done];

    expect(sendBack(board)?.id).toBe(todo.id);
  });

  it('feeds the ticked implementation column, not a column ticked elsewhere', () => {
    const todo = status(1, 10);
    const doing = status(2, 20, { isImplementation: true });
    const checkA = status(3, 30);
    const review = status(4, 40);
    const done = status(5, 50, { isTerminal: true });
    const board = [todo, doing, checkA, review, done];

    expect(sendBack(board)?.id).toBe(todo.id);
  });

  it('feeds the measured implementation column through agent checks, not the literal next one', () => {
    const todo = status(1, 10);
    const doing = status(2, 20);
    const checkA = status(3, 30);
    const checkB = status(4, 40);
    const review = status(5, 50);
    const done = status(6, 60, { isTerminal: true });
    const board = [todo, doing, checkA, checkB, review, done];

    expect(implementation(board)?.id).toBe(checkB.id);
    expect(sendBack(board)?.id).toBe(checkA.id);
  });

  it('is null on a board with no implementation column', () => {
    const review = status(1, 10);
    const done = status(2, 20, { isTerminal: true });
    const board = [review, done];

    expect(implementation(board)).toBeNull();
    expect(sendBack(board)).toBeNull();
  });

  it('is null where nothing dispatches into the ticked implementation column', () => {
    const doing = status(1, 10, { isImplementation: true });
    const review = status(2, 20);
    const done = status(3, 30, { isTerminal: true });
    const board = [doing, review, done];

    expect(implementation(board)?.id).toBe(doing.id);
    expect(sendBack(board)).toBeNull();
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
