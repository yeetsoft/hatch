import { describe, expect, it } from 'vitest';
import { cardAge, formatSpan } from './cardAge';

const MIN = 60;
const HOUR = 3600;
const DAY = 86400;

describe('formatSpan', () => {
  it('says under a minute as less than one', () => {
    expect(formatSpan(59)).toBe('<1m');
  });
  it('says minutes alone under an hour', () => {
    expect(formatSpan(60)).toBe('1m');
    expect(formatSpan(59 * MIN)).toBe('59m');
  });
  it('says hours and minutes under a day', () => {
    expect(formatSpan(HOUR)).toBe('1h');
    expect(formatSpan(14 * HOUR + 20 * MIN)).toBe('14h 20m');
    expect(formatSpan(23 * HOUR + 59 * MIN)).toBe('23h 59m');
  });
  it('says days and hours from a day', () => {
    expect(formatSpan(DAY)).toBe('1d');
    expect(formatSpan(4 * DAY + 2 * HOUR)).toBe('4d 2h');
  });
  it('drops a zero trailing unit', () => {
    expect(formatSpan(9 * HOUR)).toBe('9h');
    expect(formatSpan(2 * DAY)).toBe('2d');
  });
  it('floors each unit', () => {
    expect(formatSpan(14 * HOUR + 20 * MIN + 59)).toBe('14h 20m');
  });
});

describe('cardAge', () => {
  const status = { id: 3, name: 'In Progress' };
  const normals = [{ statusId: 3, seconds: 6 * HOUR }];
  const at = '2026-11-22T20:20:00Z';
  const card = { aged: true, enteredColumnAt: '2026-11-22T06:00:00Z' };

  it('reads the time in the column off the server clock', () => {
    expect(cardAge(card, status, normals, at)?.words).toBe('14h 20m');
  });

  it('writes the tooltip as a sentence', () => {
    expect(cardAge(card, status, normals, at)?.title).toBe(
      'In In Progress for 14h 20m. Normal here is under 6h (p90, last 30 days).',
    );
  });

  it('is null for a card the server did not mark aged', () => {
    expect(cardAge({ ...card, aged: false }, status, normals, at)).toBeNull();
    expect(cardAge({ enteredColumnAt: card.enteredColumnAt }, status, normals, at)).toBeNull();
  });

  it('is null with no normal for the column', () => {
    expect(cardAge(card, status, [], at)).toBeNull();
    expect(cardAge(card, status, undefined, at)).toBeNull();
    expect(cardAge(card, status, [{ statusId: 3, seconds: null }], at)).toBeNull();
    expect(cardAge(card, status, [{ statusId: 9, seconds: HOUR }], at)).toBeNull();
  });

  it('is null with no time of entry', () => {
    expect(cardAge({ aged: true }, status, normals, at)).toBeNull();
  });

  it('still answers with no server clock', () => {
    const old = { aged: true, enteredColumnAt: new Date(Date.now() - 2 * DAY * 1000).toISOString() };
    expect(cardAge(old, status, normals)?.words).toBe('2d');
  });
});
