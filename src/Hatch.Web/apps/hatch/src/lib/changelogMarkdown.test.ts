import { describe, expect, it } from 'vitest';
import { changelogMarkdown, escapeMarkdown } from './changelogMarkdown';
import type {
  ChangelogReport,
  ChangelogReportBucket,
  ChangelogReportEntry,
  ChangelogReportGroup,
  IssueCard,
} from '../types';

const report = (over: Partial<ChangelogReport> = {}): ChangelogReport => ({
  from: '2026-11-01T00:00:00.000Z',
  to: '2026-11-08T00:00:00.000Z',
  bucket: 'day',
  level: 'story',
  loose: false,
  buckets: [bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z', { groups: [group()] })],
  ...over,
});

const bucket = (
  start: string,
  end: string,
  over: Partial<ChangelogReportBucket> = {},
): ChangelogReportBucket => ({ start, end, groups: [], loose: [], ...over });

const group = (over: Partial<ChangelogReportGroup> = {}): ChangelogReportGroup => ({
  epic: { key: 'HA-1', projectKey: 'HA', type: 'epic', title: 'The first epic' } as IssueCard,
  closedHere: false,
  descendantCount: 3,
  stories: [],
  ...over,
});

const entry = (key: string, over: Partial<ChangelogReportEntry> = {}): ChangelogReportEntry => ({
  key,
  title: `title ${key}`,
  type: 'story',
  projectKey: 'HA',
  releaseNote: null,
  closedAt: '2026-11-04T10:00:00.000Z',
  priority: 'normal',
  ...over,
});

const options = {
  bucketLabel: (s: string) => 'Bucket ' + s.slice(0, 10),
  issueUrl: (k: string) => 'https://x.test/issues/' + k,
};

const epic = (key: string, title: string): IssueCard =>
  ({ key, projectKey: 'HA', type: 'epic', title }) as IssueCard;

const DAY_4 = '2026-11-04T00:00:00.000Z';
const DAY_5 = '2026-11-05T00:00:00.000Z';
const DAY_6 = '2026-11-06T00:00:00.000Z';
const TAG = /<[a-z!/]/i;

describe('escapeMarkdown', () => {
  it('backslash-escapes every markdown special', () => {
    expect(escapeMarkdown('\\ ` * _ [ ] < >')).toBe('\\\\ \\` \\* \\_ \\[ \\] \\< \\>');
  });

  it('leaves plain text alone', () => {
    expect(escapeMarkdown('Fix the thing (again) #12 - done.')).toBe('Fix the thing (again) #12 - done.');
  });
});

describe('changelogMarkdown', () => {
  it('writes an epic per line with what lies beneath, and no release notes', () => {
    const answer = report({
      level: 'epic',
      buckets: [
        bucket(DAY_4, DAY_5, {
          groups: [
            group({
              epic: epic('HA-1', 'The first epic'),
              descendantCount: 3,
              stories: [entry('HA-11', { releaseNote: 'A note that must not appear' })],
            }),
            group({ epic: epic('HA-2', 'The second epic'), descendantCount: 0 }),
          ],
        }),
      ],
    });

    const out = changelogMarkdown(answer, options);

    expect(out).toBe(
      [
        '## Bucket 2026-11-04',
        '',
        '- [HA-1](https://x.test/issues/HA-1) The first epic (3 beneath)',
        '- [HA-2](https://x.test/issues/HA-2) The second epic (none beneath)',
        '',
      ].join('\n'),
    );
    expect(out).not.toContain('must not appear');
    expect(out).not.toMatch(TAG);
  });

  it('nests the stories under their epic at story level, first line of the note only', () => {
    const answer = report({
      buckets: [
        bucket(DAY_4, DAY_5, {
          groups: [
            group({
              epic: epic('HA-1', 'The first epic'),
              closedHere: true,
              stories: [
                entry('HA-11', { releaseNote: 'Shipped the thing\nWith a second line' }),
                entry('HA-12'),
              ],
            }),
            group({ epic: epic('HA-2', 'The second epic'), closedHere: false }),
          ],
        }),
      ],
    });

    const out = changelogMarkdown(answer, options);

    expect(out).toBe(
      [
        '## Bucket 2026-11-04',
        '',
        '- [HA-1](https://x.test/issues/HA-1) The first epic (completed)',
        '  - [HA-11](https://x.test/issues/HA-11) Shipped the thing',
        '  - [HA-12](https://x.test/issues/HA-12) title HA-12',
        '- [HA-2](https://x.test/issues/HA-2) The second epic',
        '',
      ].join('\n'),
    );
    expect(out).not.toContain('second line');
    expect(out).not.toMatch(TAG);
  });

  it('writes loose work under Also landed, flagging only the emergency, when loose is on', () => {
    const loose = [entry('HA-21', { priority: 'emergency' }), entry('HA-22')];
    const answer = report({
      loose: true,
      buckets: [bucket(DAY_4, DAY_5, { groups: [group()], loose })],
    });

    const out = changelogMarkdown(answer, options);

    expect(out).toContain('### Also landed');
    expect(out.match(/\*\*Emergency\*\*/g)).toHaveLength(1);
    expect(out).toContain('- **Emergency** [HA-21](https://x.test/issues/HA-21) title HA-21');
    expect(out).toContain('\n- [HA-22](https://x.test/issues/HA-22) title HA-22\n');
    expect(out).not.toMatch(TAG);

    const off = changelogMarkdown({ ...answer, loose: false }, options);

    expect(off).not.toContain('Also landed');
    expect(off).not.toContain('Emergency');
  });

  it('writes a bucket with only loose work as its heading and the loose block', () => {
    const answer = report({
      loose: true,
      buckets: [bucket(DAY_4, DAY_5, { loose: [entry('HA-21')] })],
    });

    expect(changelogMarkdown(answer, options)).toBe(
      [
        '## Bucket 2026-11-04',
        '',
        '### Also landed',
        '',
        '- [HA-21](https://x.test/issues/HA-21) title HA-21',
        '',
      ].join('\n'),
    );
  });

  it('keeps the buckets in the order given and sets a heading-only bucket apart by one blank line', () => {
    const answer = report({
      buckets: [bucket(DAY_5, DAY_6), bucket(DAY_4, DAY_5, { groups: [group()] })],
    });

    const out = changelogMarkdown(answer, options);

    expect(out).toBe(
      [
        '## Bucket 2026-11-05',
        '',
        '## Bucket 2026-11-04',
        '',
        '- [HA-1](https://x.test/issues/HA-1) The first epic',
        '',
      ].join('\n'),
    );
    expect(out).not.toMatch(TAG);
  });

  it('escapes markup in titles and notes so none of it reads as a tag', () => {
    const answer = report({
      buckets: [
        bucket(DAY_4, DAY_5, {
          groups: [
            group({
              epic: epic('HA-1', '<b>x</b> snake_case'),
              stories: [entry('HA-11', { releaseNote: 'Use *stars* and [links]' })],
            }),
          ],
        }),
      ],
    });

    const out = changelogMarkdown(answer, options);

    expect(out).toContain('\\<b\\>x\\</b\\> snake\\_case');
    expect(out).toContain('Use \\*stars\\* and \\[links\\]');
    expect(out).not.toMatch(/(^|[^\\])</);
  });

  it('says nothing landed when there are no buckets', () => {
    expect(changelogMarkdown(report({ buckets: [] }), options)).toBe('Nothing landed in this window.\n');
  });
});
