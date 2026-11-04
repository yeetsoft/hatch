import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { ReportsPage } from './ReportsPage';

describe('ReportsPage', () => {
  it('lists the Timeline report with what it answers and a link to it', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <ReportsPage />
      </MemoryRouter>,
    );

    expect(html).toContain('Reports');
    expect(html).toContain('Timeline');
    expect(html).toContain('When each epic was in flight, and how long its work sat in each column.');
    expect(html).toContain('href="/reports/timeline"');
  });

  it('lists the Cumulative flow report with what it answers and a link to it', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <ReportsPage />
      </MemoryRouter>,
    );

    expect(html).toContain('Cumulative flow');
    expect(html).toContain('How much work sat in each column over time, and where it is piling up.');
    expect(html).toContain('href="/reports/cumulative-flow"');
  });

  it('lists the Changelog report with what it answers and a link to it', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <ReportsPage />
      </MemoryRouter>,
    );

    expect(html).toContain('Changelog');
    expect(html).toContain('What landed in each day, week or month, at the level a stakeholder reads.');
    expect(html).toContain('href="/reports/changelog"');
  });
});
