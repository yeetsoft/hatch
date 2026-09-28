import { describe, expect, it } from 'vitest';
import { WATCH_MS, claimMessages, messageState, watching } from './messages';
import type { Comment, IssueClaim } from '../types';

const NOW = new Date('2026-09-28T08:00:00Z');

const at = (msFromNow: number) => new Date(NOW.getTime() + msFromNow).toISOString();

const claim = (over: Partial<IssueClaim> = {}): IssueClaim => ({
  claimedBy: 'hatch',
  runner: 'somewhere:/checkouts/one',
  claimedAt: at(-600_000),
  heartbeatAt: at(0),
  chatter: null,
  chatterAt: null,
  ttlSeconds: 300,
  ...over,
});

let ids = 0;

const comment = (over: Partial<Comment> = {}): Comment => ({
  id: ++ids,
  author: 'Nathan',
  body: 'use the other table',
  kind: 'message',
  answersId: null,
  options: null,
  createdAt: at(-300_000),
  deliveredAt: null,
  deliveredTo: null,
  ...over,
});

const read = (over: Partial<Comment> = {}) =>
  comment({ deliveredAt: at(-120_000), deliveredTo: 'somewhere:/checkouts/one', ...over });

describe('messageState', () => {
  it('is waiting, in words, while a claim is live', () => {
    expect(messageState(comment(), claim(), 'HA-53', NOW)).toEqual({ state: 'waiting', words: 'not read yet' });
  });

  it('is read, with when and by which runner', () => {
    expect(messageState(read(), claim(), 'HA-53', NOW)).toEqual({
      state: 'read',
      words: 'read 2 minutes ago by somewhere:/checkouts/one',
    });
  });

  it('is read whether or not a claim is still live', () => {
    expect(messageState(read(), null, 'HA-53', NOW)?.state).toBe('read');
  });

  it('is held with no claim, and says where it goes', () => {
    expect(messageState(comment(), null, 'HA-53', NOW)).toEqual({
      state: 'held',
      words: 'not read — nobody is working HA-53; the next session on it is told',
    });
  });

  it('says read without a name when the runner was not recorded', () => {
    expect(messageState(read({ deliveredTo: null }), claim(), 'HA-53', NOW)?.words).toBe('read 2 minutes ago');
  });

  it('is nothing at all for a note, a question or an answer', () => {
    for (const kind of ['', 'question', 'answer'] as const) {
      expect(messageState(comment({ kind }), claim(), 'HA-53', NOW)).toBeNull();
    }
  });
});

describe('claimMessages', () => {
  it('is empty with no claim, however many messages there are', () => {
    expect(claimMessages([comment(), read()], null)).toEqual([]);
  });

  it('lists every unread message and every one read since the claim was taken', () => {
    const waiting = comment();
    const heard = read();

    expect(claimMessages([waiting, heard], claim())).toEqual([waiting, heard]);
  });

  it('leaves out a message read before this claim was taken', () => {
    const old = read({ deliveredAt: at(-3_600_000) });
    const waiting = comment();

    expect(claimMessages([old, waiting], claim())).toEqual([waiting]);
  });

  it('leaves out notes, questions and answers', () => {
    const notes = (['', 'question', 'answer'] as const).map((kind) => comment({ kind }));

    expect(claimMessages(notes, claim())).toEqual([]);
  });

  it('is oldest first, whatever order it was handed in', () => {
    const later = comment({ createdAt: at(-60_000) });
    const earlier = read({ createdAt: at(-500_000) });

    expect(claimMessages([later, earlier], claim())).toEqual([earlier, later]);
  });

  it('breaks a tie on id, so two sent in one instant keep their order', () => {
    const first = comment({ createdAt: at(-60_000) });
    const second = comment({ createdAt: at(-60_000) });

    expect(claimMessages([second, first], claim())).toEqual([first, second]);
  });

  it('drops a read time it cannot read rather than passing it', () => {
    expect(claimMessages([read({ deliveredAt: 'not an instant' })], claim())).toEqual([]);
  });
});

describe('watching', () => {
  it('is true while a claim is live and a message is unread', () => {
    expect(watching([comment()], claim())).toBe(true);
  });

  it('is false once everything has been read', () => {
    expect(watching([read(), read()], claim())).toBe(false);
  });

  it('is false when the claim ends, even with a message unread', () => {
    expect(watching([comment()], null)).toBe(false);
  });

  it('is false with no messages, and for notes that carry no delivery', () => {
    expect(watching([], claim())).toBe(false);
    expect(watching([comment({ kind: '' })], claim())).toBe(false);
  });

  it('polls every five seconds', () => {
    expect(WATCH_MS).toBe(5000);
  });
});
