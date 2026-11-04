import { describe, expect, it } from 'vitest';
import {
  BUCKET,
  LEVEL,
  LOOSE,
  bucketLabel,
  changelogReportEmptyWords,
  hasQuietGap,
  quietGapWords,
  resolveBucket,
  resolveLevel,
  resolveLoose,
  withBucket,
  withLevel,
  withLoose,
} from './changelogReport';

const LOCALE = 'en-GB';

const params = (query: string) => new URLSearchParams(query);

describe('bucketLabel', () => {
  it('names an hour by its date and 24-hour time', () => {
    expect(bucketLabel('2026-11-04T14:00:00Z', 'hour', 0, LOCALE)).toBe('4 Nov, 14:00');
  });

  it("shifts onto the reader's clock, into the previous local day", () => {
    expect(bucketLabel('2026-11-04T02:00:00Z', 'hour', -300, LOCALE)).toBe('3 Nov, 21:00');
  });

  it('names a day in full (en-US, whose comma the en-GB ICU data has varied on)', () => {
    expect(bucketLabel('2026-11-04T00:00:00Z', 'day', 0, 'en-US')).toBe('Wednesday, November 4, 2026');
  });

  it('names a week from its Monday to the Sunday after', () => {
    expect(bucketLabel('2026-11-02T00:00:00Z', 'week', 0, LOCALE)).toBe('2 Nov – 8 Nov 2026');
  });

  it('names a month', () => {
    expect(bucketLabel('2026-11-01T00:00:00Z', 'month', 0, LOCALE)).toBe('November 2026');
  });
});

describe('resolveBucket / withBucket', () => {
  it('defaults to day', () => {
    expect(resolveBucket(params(''))).toBe('day');
  });

  it('trims and lower-cases', () => {
    expect(resolveBucket(params('bucket=%20WEEK%20'))).toBe('week');
  });

  it('treats anything else as day', () => {
    expect(resolveBucket(params('bucket=year'))).toBe('day');
  });

  it('round-trips each size, and deletes the default', () => {
    for (const size of ['hour', 'week', 'month'] as const) {
      expect(resolveBucket(withBucket(params(''), size))).toBe(size);
    }
    expect(withBucket(params('bucket=week'), 'day').has(BUCKET)).toBe(false);
  });

  it('keeps other params and does not mutate its input', () => {
    const input = params('from=a&bucket=week');
    const next = withBucket(input, 'month');

    expect(next.get('from')).toBe('a');
    expect(next.get(BUCKET)).toBe('month');
    expect(input.toString()).toBe('from=a&bucket=week');
  });
});

describe('resolveLevel / withLevel', () => {
  it.each([
    ['', 'story'],
    ['level=story', 'story'],
    ['level=epic', 'epic'],
    ['level=%20epic%20', 'epic'],
    ['level=Epic', 'story'],
    ['level=foo', 'story'],
  ])('reads %j as %s', (query, expected) => {
    expect(resolveLevel(params(query))).toBe(expected);
  });

  it('deletes the default, sets the other, keeps other params, leaves its input alone', () => {
    const input = params('from=a&level=epic');

    expect(withLevel(input, 'story').has(LEVEL)).toBe(false);
    expect(withLevel(input, 'story').get('from')).toBe('a');
    expect(withLevel(params('from=a'), 'epic').get(LEVEL)).toBe('epic');
    expect(input.toString()).toBe('from=a&level=epic');
  });
});

describe('resolveLoose / withLoose', () => {
  it.each([
    ['', false],
    ['loose=true', true],
    ['loose=false', false],
    ['loose=1', false],
    ['loose=True', false],
  ])('reads %j as %s', (query, expected) => {
    expect(resolveLoose(params(query))).toBe(expected);
  });

  it('deletes at false, sets true, keeps other params, leaves its input alone', () => {
    const input = params('from=a&loose=true');

    expect(withLoose(input, false).has(LOOSE)).toBe(false);
    expect(withLoose(input, false).get('from')).toBe('a');
    expect(withLoose(params('from=a'), true).get(LOOSE)).toBe('true');
    expect(input.toString()).toBe('from=a&loose=true');
  });
});

describe('hasQuietGap / quietGapWords', () => {
  const newer = { start: '2026-11-05T00:00:00Z' };

  it('is false for buckets that touch', () => {
    expect(hasQuietGap(newer, { end: '2026-11-05T00:00:00Z' })).toBe(false);
  });

  it('is true across a span, and says which', () => {
    const older = { end: '2026-11-02T00:00:00Z' };

    expect(hasQuietGap(newer, older)).toBe(true);
    expect(quietGapWords(newer, older, 0, LOCALE)).toBe('Quiet from 2 Nov 2026 to 5 Nov 2026');
  });

  it("names the days on the reader's clock", () => {
    expect(
      quietGapWords(
        { start: '2026-11-05T03:00:00Z' },
        { end: '2026-11-02T03:00:00Z' },
        -300,
        LOCALE,
      ),
    ).toBe('Quiet from 1 Nov 2026 to 4 Nov 2026');
  });
});

describe('changelogReportEmptyWords', () => {
  it('says nothing landed', () => {
    expect(changelogReportEmptyWords()).toBe('Nothing landed in this window.');
  });
});
