import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { CumulativeFlowBody } from './CumulativeFlowPage';
import { ProjectsFacet } from '../components/ProjectsFacet';
import type { ColourBy } from '../lib/cumulativeFlow';
import { reportProjects } from '../lib/timeline';
import type { CumulativeFlow, Project, Status } from '../types';

/* No DOM runner, so `useLoaded` is not driven: the body is rendered from
   props, the way TimelinePage.test.tsx renders TimelineBody. */
const NOW = new Date('2026-10-06T00:00:00Z');
const EMPTY = 'Nothing was on the board in this range.';
const FOOTNOTE = 'hatch-timeline-footnote';
const UNPINNED = 'Click the chart to pin a moment.';

const statuses = [{ id: 1, name: 'To Do', color: '#3366cc' }] as Status[];
const projects = [{ id: 1, key: 'HA', name: 'Hatch', color: '#112233' }] as Project[];

const answer = (over: Partial<CumulativeFlow> = {}): CumulativeFlow => ({
  from: '2026-10-01T00:00:00.000Z',
  to: '2026-10-11T00:00:00.000Z',
  stepMinutes: 1440,
  columns: [1],
  projects: [],
  points: [{ at: '2026-10-02T00:00:00.000Z', counts: [4], projectCounts: [] }],
  unresolved: 0,
  ...over,
});

const render = (data: CumulativeFlow | null, by: ColourBy = 'column'): string =>
  renderToStaticMarkup(
    <CumulativeFlowBody answer={data} statuses={statuses} by={by} projects={projects} offsetMinutes={0} now={NOW} />,
  );

describe('CumulativeFlowBody', () => {
  it('draws the chart when there is work on the board, with no footnote at zero', () => {
    const html = render(answer());

    expect(html).toContain('class="hatch-cumulative-flow"');
    expect(html).toContain(UNPINNED);
    expect(html).not.toContain(EMPTY);
    expect(html).not.toContain(FOOTNOTE);
  });

  it('says there is nothing to show when there are no points', () => {
    const html = render(answer({ points: [] }));

    expect(html).toContain(EMPTY);
    expect(html).not.toContain('hatch-cumulative-flow"');
  });

  it('says there is nothing to show when every count everywhere is zero', () => {
    const html = render(answer({ points: [{ at: '2026-10-02T00:00:00.000Z', counts: [0], projectCounts: [] }] }));

    expect(html).toContain(EMPTY);
    expect(html).not.toContain('hatch-cumulative-flow"');
  });

  it('footnotes one unplaced column change in the singular', () => {
    const html = render(answer({ unresolved: 1 }));

    expect(html).toContain(FOOTNOTE);
    expect(html).toContain('1 column change could not be placed');
    expect(html).not.toContain('1 column changes');
  });

  it('footnotes several in the plural', () => {
    const html = render(answer({ unresolved: 3 }));

    expect(html).toContain('3 column changes could not be placed');
  });

  it('carries no footnote when nothing is unresolved', () => {
    const html = render(answer({ unresolved: 0 }));

    expect(html).not.toContain(FOOTNOTE);
  });

  it('carries the footnote on the empty state too', () => {
    const html = render(answer({ points: [], unresolved: 2 }));

    expect(html).toContain(EMPTY);
    expect(html).toContain('2 column changes could not be placed');
  });

  it('says no project is chosen for a null answer: no chart, no footnote, not the empty-range sentence', () => {
    const html = render(null);

    expect(html).toContain('No project is chosen.');
    expect(html).not.toContain('<svg');
    expect(html).not.toContain('hatch-cumulative-flow"');
    expect(html).not.toContain(FOOTNOTE);
    expect(html).not.toContain(EMPTY);
  });
});

describe('under a scope', () => {
  const all = [
    { id: 1, key: 'HA', name: 'Hatch' },
    { id: 2, key: 'AER', name: 'Aerie' },
    { id: 3, key: 'PYC', name: 'Pycnic' },
  ] as Project[];
  const scope = ['HA', 'AER'];

  it('reads and chooses the scope when no project is named', () => {
    const { chosen, narrowed } = reportProjects(new URLSearchParams(''), all, scope);

    expect(chosen.map((p) => p.key)).toEqual(['HA', 'AER']);
    expect(narrowed?.map((p) => p.key)).toEqual(['HA', 'AER']);
  });

  it('chooses nothing, so reads nothing, for an empty project param', () => {
    expect(reportProjects(new URLSearchParams('project='), all, scope).chosen).toEqual([]);
  });

  it('still reads the scope for an unknown key or no param', () => {
    expect(reportProjects(new URLSearchParams('project=ZZ'), all, scope).chosen.map((p) => p.key)).toEqual([
      'HA',
      'AER',
    ]);
    expect(reportProjects(new URLSearchParams(''), all, scope).chosen).not.toEqual([]);
  });

  it('ignores a project outside the scope', () => {
    const { chosen, narrowed } = reportProjects(new URLSearchParams('project=PYC'), all, scope);

    expect(chosen.map((p) => p.key)).toEqual(['HA', 'AER']);
    expect(narrowed?.map((p) => p.key)).toEqual(['HA', 'AER']);
  });

  it('offers only the scope in the Projects control', () => {
    const { offered, chosen } = reportProjects(new URLSearchParams('project=PYC'), all, scope);
    const html = renderToStaticMarkup(
      <ProjectsFacet projects={offered} chosen={chosen.map((p) => p.key)} onChange={() => {}} />,
    );

    expect(html).toContain('<span class="hatch-types-name">HA — Hatch</span>');
    expect(html).toContain('<span class="hatch-types-name">AER — Aerie</span>');
    expect(html).not.toContain('PYC');
  });
});
