import { describe, expect, it } from 'vitest';
import { servesWords, sinceWords } from './ambitionChanges';

const NOW = new Date('2026-11-20T12:00:00Z');

/** The instant `ms` before now. */
const before = (ms: number) => new Date(NOW.getTime() - ms).toISOString();

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

describe('sinceWords', () => {
  it.each([
    [0, 'under a minute ago'],
    [MINUTE - 1000, 'under a minute ago'],
    [MINUTE, '1 minute ago'],
    [HOUR - 1000, '59 minutes ago'],
    [HOUR, '1 hour ago'],
    [DAY - 1000, '23 hours ago'],
    [DAY, '1 day ago'],
    [3 * DAY, '3 days ago'],
  ])('reads %i ms as "%s"', (ms, said) => {
    expect(sinceWords(before(ms), NOW)).toBe(said);
  });

  it('reads an instant ahead of the clock as under a minute', () => {
    expect(sinceWords(before(-MINUTE), NOW)).toBe('under a minute ago');
  });

  it('reads an offset instant, as the server writes it', () => {
    expect(sinceWords('2026-11-20T10:00:00.1234567+00:00', NOW)).toBe('1 hour ago');
  });

  it('says nothing for an instant that does not parse', () => {
    expect(sinceWords('yesterday-ish', NOW)).toBeNull();
  });
});

describe('servesWords', () => {
  it('lists the positions, or says there are none', () => {
    expect(servesWords([2])).toBe('serves #2');
    expect(servesWords([1, 3])).toBe('serves #1, #3');
    expect(servesWords([])).toBe('serves no capability yet');
    expect(servesWords(null)).toBe('serves no capability yet');
  });
});
