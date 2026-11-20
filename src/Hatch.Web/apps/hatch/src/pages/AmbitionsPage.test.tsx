import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { AmbitionList } from './AmbitionsPage';
import type { AmbitionOpenEpic, AmbitionRow, Status } from '../types';

/* No DOM runner: the row is rendered from props, the way PlanPage.test.tsx
   does it for EpicCard. */
const statuses = [
  { id: 1, name: 'To Do', color: '#3366cc' },
  { id: 2, name: 'In Progress', color: '#cc6633' },
] as Status[];

const epic = (key: string, over: Partial<AmbitionOpenEpic> = {}): AmbitionOpenEpic => ({
  key,
  projectKey: key.split('-')[0],
  title: `title ${key}`,
  statusId: 2,
  statusName: 'In Progress',
  serves: [],
  waitsOnPerson: false,
  why: null,
  ...over,
});

const row = (key: string, over: Partial<AmbitionRow> = {}): AmbitionRow => ({
  key,
  projectKey: key.split('-')[0],
  title: `ambition ${key}`,
  statusId: 1,
  statusName: 'To Do',
  limit: 2,
  capabilities: 10,
  holds: 2,
  inFlight: 3,
  waitsOnPerson: 0,
  notStarted: 5,
  open: [],
  childBeyondScope: false,
  ...over,
});

const render = (...rows: AmbitionRow[]): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <AmbitionList ambitions={rows} statuses={statuses} />
    </MemoryRouter>,
  );

describe('AmbitionList', () => {
  it('draws its one sentence when no ambition is in scope', () => {
    const html = render();

    expect(html).toContain('No ambition in scope.');
    expect(html).not.toContain('hatch-ambition-row');
  });


  it('draws one row per ambition, each with its link and project chip', () => {
    const html = render(row('AER-1'), row('OPS-2'));

    expect(html.match(/hatch-ambition-row/g)).toHaveLength(2);
    expect(html).toContain('href="/issues/AER-1"');
    expect(html).toContain('href="/issues/OPS-2"');
    expect(html).toContain('<span class="hatch-plan-project">AER</span>');
    expect(html).toContain('<span class="hatch-plan-project">OPS</span>');
    expect(html).toContain('To Do');
  });

  it('draws one segment per non-zero count, grown in proportion, and a legend naming each', () => {
    const html = render(row('AER-1'));

    expect(html).toContain('10 capabilities');
    expect(html.match(/hatch-ambition-segment/g)).toHaveLength(3);
    expect(html).toContain('hatch-ambition-holds" style="flex-grow:2"');
    expect(html).toContain('hatch-ambition-in-flight" style="flex-grow:3"');
    expect(html).toContain('hatch-ambition-not-started" style="flex-grow:5"');
    expect(html).not.toContain('hatch-ambition-waiting');

    expect(html).toContain('2 hold');
    expect(html).toContain('3 in flight');
    expect(html).toContain('5 not started');
    // The label reads all four out; the legend has no entry for the zero.
    expect(html).not.toContain('</span>0 waiting on a person');
  });

  it('reads all four counts out in the bar label, zeros included', () => {
    const html = render(row('AER-1'));

    expect(html).toContain('role="img"');
    expect(html).toContain('aria-label="2 hold, 3 in flight, 0 waiting on a person, 5 not started"');
  });

  it('draws no bar and no legend when every count is zero, and says so', () => {
    const html = render(row('AER-1', { capabilities: 0, holds: 0, inFlight: 0, waitsOnPerson: 0, notStarted: 0 }));

    expect(html).toContain('0 capabilities');
    expect(html).toContain('This ambition has no capabilities yet.');
    expect(html).not.toContain('hatch-ambition-bar');
    expect(html).not.toContain('hatch-ambition-legend');
  });

  it('says no epic is open when there is none', () => {
    const html = render(row('AER-1', { open: [] }));

    expect(html).toContain('In flight, 0 of 2');
    expect(html).toContain('No epic is open under this ambition.');
    expect(html).not.toContain('runs 2 at once');
  });

  it('draws an open epic with its key link, column and served badges, and the count line', () => {
    const html = render(row('AER-1', { open: [epic('AER-7', { serves: [1, 3] }), epic('OPS-8')] }));

    expect(html).toContain('In flight, 2 of 2');
    expect(html).toContain('2 open, runs 2 at once');
    expect(html).toContain('href="/issues/AER-7"');
    expect(html).toContain('title AER-7');
    expect(html).toContain('In Progress');
    expect(html).toContain('<span class="hatch-capability-badge">#1</span>');
    expect(html).toContain('<span class="hatch-capability-badge">#3</span>');
    // An epic serving nothing draws a dash.
    expect(html).toContain('<span class="text-muted">—</span>');
    expect(html).not.toContain('No epic is open');
  });

  it('draws a column the list lacks as muted text', () => {
    const html = render(row('AER-1', { statusId: 99, statusName: 'Gone' }));

    expect(html).toContain('<span class="text-muted">Gone</span>');
  });
});
