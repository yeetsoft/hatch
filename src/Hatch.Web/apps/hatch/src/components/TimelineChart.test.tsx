import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { TimelineChart } from './TimelineChart';
import { makeScale, segmentTitle, ticks, xOf } from '../lib/timeline';
import type { IssueCard, Status, Timeline, TimelineEntry, TimelineSegment } from '../types';

/* No DOM here, so this is structural - react-dom/server, the way
   AttentionHuman.test.tsx reads. Class names are matched as whole attribute
   values: `hatch-timeline-label` is a substring of `-labels`, `-segment` of
   `-segment--other`, `-tick` of `-ticks`. */
const LOCALE = 'en-US';
const scale = makeScale('2026-10-01T00:00:00.000Z', '2026-10-11T00:00:00.000Z');
const OFFSET = 0;
const NOW = new Date('2026-10-06T00:00:00Z');

const statuses = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc6633' },
] as Status[];

const segment = (statusId: number, from: string, to: string | null): TimelineSegment => ({ statusId, from, to });

const entry = (
  key: string,
  segments: TimelineSegment[] = [],
  children: TimelineEntry[] = [],
  type: IssueCard['type'] = 'story',
): TimelineEntry => ({
  issue: { key, type, title: `title ${key}` } as IssueCard,
  createdBy: 'someone',
  isLeaf: children.length === 0,
  segments,
  children,
});

const epic = entry(
  'HA-1',
  [segment(1, '2026-10-02T00:00:00.000Z', '2026-10-04T00:00:00.000Z'), segment(2, '2026-10-06T00:00:00.000Z', null)],
  [entry('HA-2', [segment(2, '2026-10-03T00:00:00.000Z', '2026-10-05T00:00:00.000Z')])],
  'epic',
);
const other = entry('other', [segment(0, '2026-10-02T00:00:00.000Z', '2026-10-08T00:00:00.000Z')], [], 'epic');
other.issue.title = 'Other';

const timeline: Timeline = {
  from: '2026-10-01T00:00:00.000Z',
  to: '2026-10-11T00:00:00.000Z',
  epics: [epic],
  other,
  creators: [],
  unresolved: 0,
};

function render(over: { expanded?: string[]; hidden?: string[]; now?: Date; timeline?: Timeline } = {}): string {
  return renderToStaticMarkup(
    <MemoryRouter>
      <TimelineChart
        timeline={over.timeline ?? timeline}
        statuses={statuses}
        scale={scale}
        offsetMinutes={OFFSET}
        now={over.now ?? NOW}
        expanded={new Set(over.expanded ?? [])}
        onToggle={() => {}}
        hidden={new Set(over.hidden ?? [])}
        onHide={() => {}}
        locale={LOCALE}
      />
    </MemoryRouter>,
  );
}

const count = (html: string, className: string) => html.split(`class="${className}"`).length - 1;
const laneOf = (html: string, index: number) => html.split('class="hatch-timeline-lane"')[index + 1].split('</svg>')[0];
const rects = (lane: string) =>
  [...lane.matchAll(/<rect class="([^"]*)"[^>]* x="([^"]+)"[^>]* width="([^"]+)"[^>]*>(?:<title>(.*?)<\/title>)?/g)].map(
    ([, className, x, width, title]) => ({ className, x: Number(x), width: Number(width), title }),
  );

describe('TimelineChart', () => {
  it('draws a label and a lane for the epic and for Other, and no children, while collapsed', () => {
    const html = render();

    expect(count(html, 'hatch-timeline-label')).toBe(2);
    expect(count(html, 'hatch-timeline-lane')).toBe(2);
    expect(html).not.toContain('HA-2');
    expect(html.indexOf('HA-1')).toBeLessThan(html.indexOf('Other'));
  });

  it('gives Other a title and no key or link', () => {
    const html = render();

    expect(html).not.toContain('/issues/other');
    expect(count(html, 'hatch-timeline-key')).toBe(1);
    expect(html).toContain('aria-label="Children of HA-1"');
    expect(html).toContain('title="Children of HA-1"');
    expect(html).toContain('aria-label="Other"');
  });

  it('shows the children of an open row, and says it is open', () => {
    const open = render({ expanded: ['HA-1'] });

    expect(count(open, 'hatch-timeline-label')).toBe(3);
    expect(open).toContain('/issues/HA-2');
    expect(open).toMatch(/aria-expanded="true"/);
    expect(open).toContain('--depth:1');
    expect(render()).toMatch(/aria-expanded="false"/);
  });

  it('draws a toggle only on a row with children', () => {
    const leaf = render({ timeline: { ...timeline, epics: [entry('HA-9')], other: null } });

    expect(count(leaf, 'hatch-timeline-toggle')).toBe(0);
    expect(count(render(), 'hatch-timeline-toggle')).toBe(1);
  });

  it('draws a Hide button on each top-level row and on no child row', () => {
    const html = render();

    expect(html).toContain('aria-label="Hide HA-1"');
    expect(html).toContain('aria-label="Hide Other"');
    expect(count(html, 'hatch-timeline-hide')).toBe(2);

    const open = render({ expanded: ['HA-1'] });

    expect(count(open, 'hatch-timeline-label')).toBe(3);
    expect(count(open, 'hatch-timeline-hide')).toBe(2);
    expect(open).not.toContain('aria-label="Hide HA-2"');
  });

  it('leaves a hidden key out of the labels and the lanes', () => {
    const html = render({ hidden: ['HA-1'] });

    expect(html).not.toContain('HA-1');
    expect(html).not.toContain('title HA-1');
    expect(count(html, 'hatch-timeline-label')).toBe(1);
    expect(count(html, 'hatch-timeline-lane')).toBe(1);
    expect(html).toContain('aria-label="Hide Other"');
  });

  it('places each segment by xOf, and runs an unended one to the right edge', () => {
    const drawn = rects(laneOf(render(), 0));

    expect(drawn).toHaveLength(2);
    expect(drawn[0].x).toBeCloseTo(xOf(scale, '2026-10-02T00:00:00.000Z'), 6);
    expect(drawn[0].width).toBeCloseTo(
      xOf(scale, '2026-10-04T00:00:00.000Z') - xOf(scale, '2026-10-02T00:00:00.000Z'),
      6,
    );
    expect(drawn[1].x + drawn[1].width).toBeCloseTo(100, 6);
  });

  it("holds only a parent's own segments in its lane", () => {
    const html = render({ expanded: ['HA-1'] });

    expect(rects(laneOf(html, 0))).toHaveLength(2);
    expect(rects(laneOf(html, 1))).toHaveLength(1);
  });

  it('paints a segment from its status and Other neutrally', () => {
    const html = render();
    const [first] = rects(laneOf(html, 0));
    const [grey] = rects(laneOf(html, 1));

    expect(first.className).toBe('hatch-timeline-segment');
    expect(laneOf(html, 0)).toContain('--status-color:#3366cc');
    expect(grey.className).toBe('hatch-timeline-segment hatch-timeline-segment--other');
    expect(laneOf(html, 1)).not.toContain('--status-color');
  });

  it('titles each segment with segmentTitle', () => {
    const [done, running] = rects(laneOf(render(), 0));

    expect(done.title).toBe(segmentTitle('To Do', epic.segments[0], LOCALE));
    expect(running.title).toBe(segmentTitle('In Progress', epic.segments[1], LOCALE));
  });

  it('draws the today line only while now is inside the window', () => {
    const inside = render();

    expect(count(inside, 'hatch-timeline-today')).toBe(1);
    expect(inside).toMatch(/class="hatch-timeline-today" style="left:50%"/);
    expect(render({ now: new Date('2026-12-01T00:00:00Z') })).not.toContain('hatch-timeline-today');
  });

  it('labels the ruler with the ticks', () => {
    const html = render();
    const ruler = html.split('class="hatch-timeline-ruler"')[1].split('class="hatch-timeline-lane"')[0];
    const labels = [...ruler.matchAll(/<span class="hatch-timeline-tick"[^>]*>(.*?)<\/span>/g)].map((m) => m[1]);

    expect(labels).toEqual(ticks(scale, OFFSET, LOCALE).map((tick) => tick.label));
    expect(labels.length).toBeGreaterThan(0);
  });
});
