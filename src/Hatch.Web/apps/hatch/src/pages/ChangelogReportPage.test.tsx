import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { CopyMarkdownButton } from '../components/CopyMarkdownButton';
import { ProjectsFacet } from '../components/ProjectsFacet';
import { ChangelogReportBody } from './ChangelogReportPage';
import { changelogReportEmptyWords, quietGapWords } from '../lib/changelogReport';
import { reportProjects } from '../lib/timeline';
import type {
  ChangelogReport,
  ChangelogReportBucket,
  ChangelogReportEntry,
  ChangelogReportGroup,
  IssueCard,
  Project,
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

const render = (answer: ChangelogReport | null, level: 'epic' | 'story' = 'story', loose = false): string =>
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

    expect(html).toMatch(/<a href="\/issues\/HA-1"[^>]*>The first epic<\/a>/);
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

  it('says no project is chosen for a null report, ahead of the empty-range sentence', () => {
    const html = render(null);

    expect(html).toContain('No project is chosen.');
    expect(html).not.toContain('hatch-changelog-day');
    expect(html).not.toContain(changelogReportEmptyWords());
  });
});

describe('CopyMarkdownButton', () => {
  /* The press path - the clipboard write, "Copied", a refusal - needs a DOM and
     is covered by the driver check. */
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is not drawn where the clipboard API is not there', () => {
    vi.stubGlobal('navigator', {});

    expect(renderToStaticMarkup(<CopyMarkdownButton markdown="x" />)).not.toContain('Copy as Markdown');
  });

  it('is a button saying Copy as Markdown where it is', () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn() } });

    const html = renderToStaticMarkup(<CopyMarkdownButton markdown="x" />);

    expect(html).toContain('<button');
    expect(html).toContain('Copy as Markdown');
  });
});

describe('under a scope', () => {
  const projects = [
    { id: 1, key: 'HA', name: 'Hatch' },
    { id: 2, key: 'AER', name: 'Aerie' },
    { id: 3, key: 'PYC', name: 'Pyc' },
  ] as Project[];
  const scope = ['HA', 'AER'];

  it('reads for the scope\'s projects alone', () => {
    const { chosen, narrowed } = reportProjects(new URLSearchParams(), projects, scope);

    expect(chosen.map((p) => p.key)).toEqual(['HA', 'AER']);
    expect(narrowed?.map((p) => p.id)).toEqual([1, 2]);
  });

  it('chooses nothing, so reads nothing, for an empty project param', () => {
    expect(reportProjects(new URLSearchParams('project='), projects, scope).chosen).toEqual([]);
  });

  it('still reads the scope for an unknown key or no param', () => {
    expect(reportProjects(new URLSearchParams('project=ZZ'), projects, scope).chosen.map((p) => p.key)).toEqual([
      'HA',
      'AER',
    ]);
    expect(reportProjects(new URLSearchParams(''), projects, scope).chosen).not.toEqual([]);
  });

  it('ignores a project param outside the scope', () => {
    const { chosen, narrowed } = reportProjects(new URLSearchParams('project=PYC'), projects, scope);

    expect(chosen.map((p) => p.key)).toEqual(['HA', 'AER']);
    expect(narrowed?.map((p) => p.id)).toEqual([1, 2]);
  });

  it('sends no project filter with no scope and no param', () => {
    expect(reportProjects(new URLSearchParams(), projects, []).narrowed).toBeUndefined();
  });

  it('offers only the scope\'s projects in the facet', () => {
    const { offered, chosen } = reportProjects(new URLSearchParams(), projects, scope);

    const html = renderToStaticMarkup(
      <ProjectsFacet projects={offered} chosen={chosen.map((p) => p.key)} onChange={() => {}} />,
    );

    expect(html).toContain('HA — Hatch');
    expect(html).toContain('AER — Aerie');
    expect(html).not.toContain('PYC');
  });
});
