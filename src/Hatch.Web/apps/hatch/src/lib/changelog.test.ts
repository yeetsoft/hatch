import { describe, expect, it } from 'vitest';
import {
  appendWindow,
  canLoadOlder,
  changelogEmptyWords,
  dayHeading,
  fixLine,
  monthBefore,
  monthLabel,
  nextBefore,
  parseBefore,
} from './changelog';
import type { ChangelogDayDto, ChangelogDto } from '../types';

describe('changelogEmptyWords', () => {
  it('says nothing has shipped yet', () => {
    expect(changelogEmptyWords()).toBe('Nothing has shipped yet.');
  });
});

describe('dayHeading', () => {
  it('names the UTC day, whatever the test machine\'s own zone is', () => {
    expect(dayHeading('2026-10-13', 'en-US')).toBe('Tuesday, October 13, 2026');
  });

  it('does not slip a day at the UTC boundary', () => {
    expect(dayHeading('2026-01-01', 'en-US')).toBe('Thursday, January 1, 2026');
  });
});

describe('fixLine', () => {
  it('reads the release note\'s first line', () => {
    expect(fixLine({ title: 'Title', releaseNote: 'First line.\n\nMore.' })).toBe('First line.');
  });

  it('drops a trailing \\r on a \\r\\n note', () => {
    expect(fixLine({ title: 'Title', releaseNote: 'First line.\r\nMore.' })).toBe('First line.');
  });

  it('skips leading blank lines and whitespace to the first real line', () => {
    expect(fixLine({ title: 'Title', releaseNote: '\n  \nFirst real line.\nMore.' })).toBe('First real line.');
  });

  it('falls back to the title when the note is null', () => {
    expect(fixLine({ title: 'Title', releaseNote: null })).toBe('Title');
  });

  it('falls back to the title when the note is empty', () => {
    expect(fixLine({ title: 'Title', releaseNote: '' })).toBe('Title');
  });

  it('falls back to the title when the note is only whitespace', () => {
    expect(fixLine({ title: 'Title', releaseNote: '  \n ' })).toBe('Title');
  });
});

const day = (date: string): ChangelogDayDto => ({ date, features: [], fixes: [] });

const windowOf = (dates: string[], over: Partial<ChangelogDto> = {}): ChangelogDto => ({
  days: dates.map(day),
  hasOlder: false,
  oldest: dates[dates.length - 1] ?? null,
  months: [],
  ...over,
});

describe('appendWindow', () => {
  it('leaves the first window days in place and ahead of the second', () => {
    const first = windowOf(['2026-10-13', '2026-10-12'], { hasOlder: true });
    const second = windowOf(['2026-10-09', '2026-10-08']);

    const held = appendWindow(first, second);

    expect(held.days.map((d) => d.date)).toEqual(['2026-10-13', '2026-10-12', '2026-10-09', '2026-10-08']);
    expect(held.days[0]).toBe(first.days[0]);
    expect(held.days[1]).toBe(first.days[1]);
  });

  it('does not list a day twice when the same window is appended twice', () => {
    const first = windowOf(['2026-10-13'], { hasOlder: true });
    const second = windowOf(['2026-10-09']);

    const held = appendWindow(appendWindow(first, second), second);

    expect(held.days.map((d) => d.date)).toEqual(['2026-10-13', '2026-10-09']);
  });

  it('returns the window itself when nothing is held', () => {
    const only = windowOf(['2026-10-13']);
    expect(appendWindow(null, only)).toBe(only);
  });

  it('takes hasOlder and oldest from the second window', () => {
    const first = windowOf(['2026-10-13'], { hasOlder: true });
    const second = windowOf(['2026-10-09'], { hasOlder: true });

    const held = appendWindow(first, second);

    expect(held.hasOlder).toBe(true);
    expect(held.oldest).toBe('2026-10-09');
    expect(appendWindow(first, windowOf(['2026-10-09'])).hasOlder).toBe(false);
  });

  it('does not change what was held', () => {
    const first = windowOf(['2026-10-13'], { hasOlder: true });
    appendWindow(first, windowOf(['2026-10-09']));
    expect(first.days.map((d) => d.date)).toEqual(['2026-10-13']);
  });
});

describe('nextBefore', () => {
  it('is the oldest day of what is held, so the window below has no overlap and no gap', () => {
    const held = appendWindow(
      windowOf(['2026-10-13', '2026-10-12'], { hasOlder: true }),
      windowOf(['2026-10-09', '2026-10-08'], { hasOlder: true }),
    );

    expect(nextBefore(held)).toBe('2026-10-08');
    expect(held.days[held.days.length - 1].date).toBe(nextBefore(held));
  });

  it('is null when there is nothing older, or no oldest day', () => {
    expect(nextBefore(windowOf(['2026-10-13']))).toBeNull();
    expect(nextBefore(windowOf([], { hasOlder: true }))).toBeNull();
  });
});

describe('canLoadOlder', () => {
  it('is true only while the answer says there is an older window', () => {
    expect(canLoadOlder(windowOf(['2026-10-13'], { hasOlder: true }))).toBe(true);
    expect(canLoadOlder(windowOf(['2026-10-13']))).toBe(false);
  });
});

describe('monthBefore', () => {
  it('asks for the window that ends after the last day of the month', () => {
    expect(monthBefore('2026-03')).toBe('2026-04-01');
  });

  it('rolls December into the next year', () => {
    expect(monthBefore('2026-12')).toBe('2027-01-01');
  });

  it('keeps the month zero-padded', () => {
    expect(monthBefore('2026-09')).toBe('2026-10-01');
  });
});

describe('monthLabel', () => {
  it('reads as a month and a count', () => {
    expect(monthLabel({ month: '2026-10', count: 12 }, 'en-US')).toBe('October 2026 · 12');
  });

  it('does not slip a month at the UTC boundary', () => {
    expect(monthLabel({ month: '2026-01', count: 1 }, 'en-US')).toBe('January 2026 · 1');
  });
});

describe('parseBefore', () => {
  it('is null when absent or blank', () => {
    expect(parseBefore(new URLSearchParams(''))).toBeNull();
    expect(parseBefore(new URLSearchParams('before=%20%20'))).toBeNull();
  });

  it('is the value when present', () => {
    expect(parseBefore(new URLSearchParams('before=2026-04-01'))).toBe('2026-04-01');
  });
});
