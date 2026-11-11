import { describe, expect, it, vi } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { ConsumptionReportBody } from './ConsumptionReportPage';
import { UNPRICED_TITLE } from '../lib/workLog';
import type { ConsumptionReport, ConsumptionRow, ConsumptionSort, Project, Status, WorkLogTotals } from '../types';

/* No DOM runner and no fetch: the body is rendered from props, as
   ChangelogReportPage.test.tsx does with ChangelogReportBody. */
const totals = (over: Partial<WorkLogTotals> = {}): WorkLogTotals => ({
  sessions: 4,
  errors: 0,
  inputTokens: 900,
  outputTokens: 280,
  cacheCreationTokens: 3_200,
  cacheReadTokens: 59_000,
  totalTokens: 1_400_000,
  costUsd: 3.41,
  unpricedSessions: 0,
  durationMs: 11_520_000,
  ...over,
});

const row = (key: string, over: Partial<ConsumptionRow> = {}): ConsumptionRow => ({
  key,
  title: `title ${key}`,
  type: 'epic',
  projectKey: 'HA',
  statusId: 3,
  parentKey: null,
  totals: totals(),
  firstSessionAt: '2026-10-01T10:00:00.000Z',
  lastSessionAt: '2026-10-07T10:00:00.000Z',
  ...over,
});

const report = (over: Partial<ConsumptionReport> = {}): ConsumptionReport => ({
  from: '2026-08-01T00:00:00.000Z',
  to: '2026-11-01T00:00:00.000Z',
  type: 'epic',
  sort: 'activity',
  limit: 100,
  matched: 2,
  totals: totals({ sessions: 8, totalTokens: 2_800_000, costUsd: 6.82, durationMs: 23_040_000 }),
  firstSessionAt: '2026-08-19T10:00:00.000Z',
  lastSessionAt: '2026-10-07T10:00:00.000Z',
  rows: [row('HA-1'), row('HA-2')],
  ...over,
});

const projects = [{ id: 1, key: 'HA', name: 'Hatch', color: '#336699', icon: null } as unknown as Project];
const statuses = [{ id: 3, name: 'In Progress' } as Status];

const render = (answer: ConsumptionReport | null, sort: ConsumptionSort = 'activity'): string =>
  renderToStaticMarkup(
    <MemoryRouter>
      <ConsumptionReportBody
        report={answer}
        projects={projects}
        statuses={statuses}
        sort={sort}
        onSort={vi.fn()}
        locale="en-US"
      />
    </MemoryRouter>,
  );

describe('ConsumptionReportBody', () => {
  it('draws a row per issue, in the server order, linked and with its status name', () => {
    const html = render(report());

    expect(html).toContain('href="/issues/HA-1"');
    expect(html).toContain('title HA-2');
    expect(html.indexOf('HA-1')).toBeLessThan(html.indexOf('HA-2'));
    expect(html).toContain('In Progress');
    expect(html).toContain('1.4M');
    expect(html).toContain('$3.41');
  });

  it('falls back to the raw id for a column the board no longer carries', () => {
    const html = render(report({ rows: [row('HA-1', { statusId: 99 })] }));

    expect(html).toContain('>99<');
  });

  it('says the totals over the whole filter', () => {
    expect(render(report())).toContain('2 issues · 8 sessions · 2.8M tokens · $6.82 · 6h 24m');
  });

  it("says the runner time humanised, in the table's cells", () => {
    const html = render(
      report({
        rows: [
          row('HA-1', { totals: totals({ durationMs: 48_000 }) }),
          row('HA-2', { totals: totals({ durationMs: 303_000 }) }),
        ],
      }),
    );

    expect(html).toContain('>48s<');
    expect(html).toContain('>5m 03s<');
    expect(render(report())).toContain('>3h 12m<');
  });

  it('draws a dash with the unpriced title, never $0, on a row with no priced session', () => {
    const html = render(
      report({
        rows: [row('HA-1', { totals: totals({ sessions: 2, unpricedSessions: 2, costUsd: 0 }) })],
      }),
    );

    expect(html).toContain(`<td title="${UNPRICED_TITLE}">—</td>`);
    expect(html).not.toContain('$0<');
  });

  it('draws the floor and the count on a row with some unpriced sessions', () => {
    const html = render(report({ rows: [row('HA-1', { totals: totals({ unpricedSessions: 1 }) })] }));

    expect(html).toContain('$3.41 (1 unpriced)');
  });

  it('says the cap above the table when matched exceeds the rows drawn', () => {
    const html = render(report({ matched: 240 }));

    expect(html).toContain('The first 2 of 240 issues. The totals cover all 240.');
    expect(html.indexOf('The first 2 of 240')).toBeLessThan(html.indexOf('<table'));
  });

  it('says no cap when every match is drawn', () => {
    expect(render(report())).not.toContain('The first');
  });

  it('marks the active sortable head descending and the rest none', () => {
    const html = render(report(), 'cost');

    expect(html).toContain('aria-sort="descending"><button type="button" class="hatch-leaderboard-sort active">Notional USD');
    expect(html.match(/aria-sort="descending"/g)).toHaveLength(1);
    expect(html.match(/aria-sort="none"/g)).toHaveLength(3);
    expect(html).toContain('Last session');
    expect(html).toContain('Runner time');
  });

  it('says nothing was ever logged', () => {
    const html = render(report({ firstSessionAt: null, lastSessionAt: null, totals: totals({ sessions: 0 }), rows: [] }));

    expect(html).toContain('Nothing has ever been logged under this filter.');
    expect(html).not.toContain('<table');
  });

  it('says nothing ran in this window, and where the log does run', () => {
    const html = render(report({ totals: totals({ sessions: 0 }), rows: [], matched: 0 }));

    expect(html).toContain('No issue consumed anything in this window.');
    expect(html).toContain('The log runs from Aug 19 to Oct 7.');
    expect(html).not.toContain('<table');
  });

  it('says no project is chosen when there is no report', () => {
    expect(render(null)).toContain('No project is chosen.');
  });
});
