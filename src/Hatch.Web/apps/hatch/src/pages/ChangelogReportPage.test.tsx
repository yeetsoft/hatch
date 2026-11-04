import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { ChangelogReportBody } from './ChangelogReportPage';
import { quietGapWords } from '../lib/changelogReport';
import type {
  ChangelogReport,
  ChangelogReportBucket,
  ChangelogReportEntry,
  ChangelogReportGroup,
  IssueCard,
} from '../types';

/* No DOM runner and no fetch: the body is rendered from props, as
   TimelinePage.test.tsx does with TimelineBody. */
const EMERGENCY_TITLE = 'title="Emergency - ';

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

const render = (answer: ChangelogReport, level: 'epic' | 'story' = 'story', loose = false): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ChangelogReportBody
        report={answer}
        projects={[]}
        level={level}
        loose={loose}
        offsetMinutes={0}
        locale="en-US"
      />
    </MemoryRouter>,
  );

describe('ChangelogReportBody', () => {
  it('draws a card per epic at epic level: its title, never a release note, and what sat beneath', () => {
    const html = render(
      report({
        level: 'epic',
        buckets: [
          bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z', {
            groups: [
              group({ stories: [entry('HA-5', { releaseNote: 'A note nobody draws' })] }),
              group({
                epic: { key: 'HA-2', projectKey: 'HA', type: 'epic', title: 'The second epic' } as IssueCard,
                descendantCount: 0,
              }),
            ],
          }),
        ],
      }),
      'epic',
    );

    expect(html).toContain('The first epic');
    expect(html).toContain('The second epic');
    expect(html).toContain('3 beneath');
    expect(html).toContain('none beneath');
    expect(html).toContain('href="/issues/HA-1"');
    expect(html).not.toContain('A note nobody draws');
  });

  it('draws the epic title as a link at story level, Completed only when closed here, and each story line', () => {
    const stories = [
      entry('HA-5', { releaseNote: 'First line of the note\nsecond line' }),
      entry('HA-6'),
    ];
    const html = render(
      report({
        buckets: [
          bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z', {
            groups: [group({ closedHere: true, stories })],
          }),
        ],
      }),
    );

    expect(html).toContain('<a href="/issues/HA-1">The first epic</a>');
    expect(html).toContain('Completed');
    expect(html).toContain('First line of the note');
    expect(html).not.toContain('second line');
    expect(html).toContain('title HA-6');

    expect(render(report())).not.toContain('Completed');
  });

  it('draws a story-level group with no stories as its heading alone', () => {
    const html = render(report());

    expect(html).toContain('The first epic');
    expect(html).not.toContain('<ul');
  });

  it('draws Also landed with a row per entry when loose is on, the emergency one marked', () => {
    const html = render(
      report({
        loose: true,
        buckets: [
          bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z', {
            loose: [entry('HA-7', { priority: 'emergency' }), entry('HA-8')],
          }),
        ],
      }),
      'story',
      true,
    );

    expect(html).toContain('Also landed');
    expect(html.match(/<li /g)).toHaveLength(2);

    const [emergency, normal] = html.split('<li ').slice(1);
    expect(emergency).toContain('hatch-changelog-report-entry emergency');
    expect(emergency).toContain(EMERGENCY_TITLE);
    expect(normal).toContain('hatch-changelog-report-entry');
    expect(normal).not.toContain('emergency');
    expect(normal).not.toContain(EMERGENCY_TITLE);
  });

  it('leaves Also landed out when loose is off, whatever the bucket holds', () => {
    const html = render(
      report({
        buckets: [bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z', { loose: [entry('HA-7')] })],
      }),
    );

    expect(html).not.toContain('Also landed');
    expect(html).not.toContain('HA-7');
  });

  it('says so when no bucket holds anything', () => {
    expect(render(report({ buckets: [] }))).toContain('Nothing landed in this window.');
  });

  it('draws a quiet gap between buckets that leave one, newest first, and none between buckets that touch', () => {
    const newer = bucket('2026-11-04T00:00:00.000Z', '2026-11-05T00:00:00.000Z');
    const older = bucket('2026-10-20T00:00:00.000Z', '2026-10-21T00:00:00.000Z');
    const touching = bucket('2026-11-03T00:00:00.000Z', '2026-11-04T00:00:00.000Z');

    const gapped = render(report({ buckets: [newer, older] }));
    expect(gapped).toContain('hatch-changelog-report-gap');
    expect(gapped).toContain('Quiet from ');
    expect(gapped).toContain(quietGapWords(newer, older, 0, 'en-US'));

    const joined = render(report({ buckets: [newer, touching] }));
    expect(joined).not.toContain('hatch-changelog-report-gap');
    expect(joined).not.toContain('Quiet from ');
  });

  it('heads each bucket with bucketLabel', () => {
    expect(render(report())).toContain('<h2>Wednesday, November 4, 2026</h2>');
  });
});
