import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { MoreFilters } from './MoreFilters';
import { DEFAULT_FILTER } from '../lib/filter';
import type { CardFilter } from '../lib/filter';

const filers = [
  { name: 'Ada', count: 4 },
  { name: 'zoe', count: 1 },
];

const render = (filter: CardFilter = DEFAULT_FILTER) =>
  renderToStaticMarkup(<MoreFilters filter={filter} assignees={[]} filedBy={filers} onChange={() => {}} />);

describe('MoreFilters', () => {
  it('draws the Assignee and Filed by groups, with a count per filer', () => {
    const html = render();
    expect(html).toContain('aria-label="Assignee"');
    expect(html).toContain('aria-label="Filed by"');
    expect(html).toContain('hatch-more-filters-title">Assignee<');
    expect(html).toContain('hatch-more-filters-title">Filed by<');
    expect(html).toContain('<span class="hatch-types-count">4</span>');
    expect(html).toContain('<span class="hatch-types-count">1</span>');
  });

  it('is not lit, and says only More filters, at the default', () => {
    const html = render();
    expect(html).not.toContain('hatch-facet--lit');
    expect(html).toContain('title="More filters"');
  });

  it('is lit for the assignee alone', () => {
    const html = render({ ...DEFAULT_FILTER, assignee: 'unassigned' });
    expect(html).toContain('hatch-facet--lit');
    expect(html).toContain('title="More filters - Assignee: Unassigned"');
  });

  it('is lit for Filed by alone', () => {
    const html = render({ ...DEFAULT_FILTER, filedBy: ['Ada'] });
    expect(html).toContain('hatch-facet--lit');
    expect(html).toContain('title="More filters - Filed by: Ada"');
  });

  it('names both in the tooltip when both are set', () => {
    const html = render({ ...DEFAULT_FILTER, assignee: 'unassigned', filedBy: [] });
    expect(html).toContain('title="More filters - Assignee: Unassigned; Filed by: None"');
  });
});
