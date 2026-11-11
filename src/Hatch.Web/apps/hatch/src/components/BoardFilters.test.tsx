import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BoardFilters } from './BoardFilters';
import { DEFAULT_FILTER } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import type { Project } from '../types';

const noop = () => {};

const projects = [
  { key: 'GL', name: 'glasshole' },
  { key: 'HA', name: 'Hatch' },
] as Project[];

function render(
  filter: CardFilter = DEFAULT_FILTER,
  collapsed = false,
  over: { scoped?: boolean; projects?: Project[] } = {},
) {
  return renderToStaticMarkup(
    <BoardFilters
      filter={filter}
      onChange={noop}
      assignees={[]}
      projects={over.projects ?? projects}
      cards={[]}
      showing={3}
      total={9}
      collapsed={collapsed}
      scoped={over.scoped}
      trailing={<span>trail</span>}
    />,
  );
}

describe('BoardFilters', () => {
  it('has a short placeholder, so the box can rest narrow', () => {
    expect(render()).toContain('placeholder="Search…"');
  });

  it('calls the switch Needs me', () => {
    const html = render();
    expect(html).toContain('>Needs me<');
    expect(html).not.toContain('Waiting on me');
  });

  it('closes the Project chooser on the key and keeps the names in its list', () => {
    const html = render({ ...DEFAULT_FILTER, project: 'GL' });
    const shown = [...html.matchAll(/<span class="hatch-facet__option hatch-facet__option--shown">([^<]*)</g)];
    // Project's, then Contributions' own (All).
    expect(shown.map((m) => m[1])).toEqual(['GL', 'All']);
    expect(html).toContain('>GL — glasshole</option>');
  });

  it('puts Assignee behind a More filters button on desktop, at rest while it is anyone', () => {
    const html = render();
    expect(html).toContain('aria-label="More filters"');
    expect(html).not.toContain('hatch-facet__label">Assignee');
    expect(html).not.toMatch(/hatch-facet--lit hatch-more-filters/);
  });

  it('lights the More filters button, and says why, while the assignee is set', () => {
    const html = render({ ...DEFAULT_FILTER, assignee: 'unassigned' });
    expect(html).toMatch(/hatch-facet hatch-facet--lit hatch-more-filters/);
    expect(html).toContain('title="More filters - Assignee:');
  });

  it('counts and offers the icon Reset for a filter that is only behind the button', () => {
    const html = render({ ...DEFAULT_FILTER, assignee: 'unassigned' });
    expect(html).toContain('3 of 9');
    expect(html).toContain('aria-label="Reset filters"');
    expect(html).toContain('title="Reset filters"');
  });

  it('draws neither count nor Reset at the default', () => {
    const html = render();
    expect(html).not.toContain('3 of 9');
    expect(html).not.toContain('Reset filters');
  });

  it('keeps Assignee inline inside the Filters disclosure on a phone, with no More filters button', () => {
    const html = render(DEFAULT_FILTER, true);
    const details = html.match(/<details[\s\S]*<\/details>/)![0];
    expect(details).toContain('hatch-facet__label">Assignee');
    expect(html).not.toContain('More filters');
  });

  it('reads All in scope under a scope', () => {
    const html = render(DEFAULT_FILTER, false, { scoped: true });
    expect(html).toContain('>All in scope</option>');
    expect(html).not.toContain('>All projects</option>');
  });

  it('reads All projects with no scope', () => {
    const html = render();
    expect(html).toContain('>All projects</option>');
  });

  it('shows no Project facet for one offered project', () => {
    const html = render(DEFAULT_FILTER, false, { projects: [projects[0]] });
    expect(html).not.toContain('hatch-facet__label">Project');
  });

  it('offers Contributions as All, Only and Hidden, closed on All', () => {
    const html = render();
    expect(html).toContain('hatch-facet__label">Contributions');
    expect(html).toContain('>With everything</option>');
    expect(html).toContain('>Alone</option>');
    expect(html).toContain('>Hidden</option>');
    const shown = [...html.matchAll(/<span class="hatch-facet__option hatch-facet__option--shown">([^<]*)</g)];
    expect(shown.map((m) => m[1])).toEqual(['All', 'All']);
  });

  it('lights Contributions, counts and offers Reset once it is not All', () => {
    const html = render({ ...DEFAULT_FILTER, contributions: 'only' });
    expect(html).toContain('3 of 9');
    expect(html).toContain('aria-label="Reset filters"');
    expect(render()).not.toContain('Reset filters');
  });

  it('keeps Contributions inside the phone disclosure', () => {
    const html = render(DEFAULT_FILTER, true);
    const details = html.slice(html.indexOf('<details'));
    expect(details).toContain('hatch-facet__label">Contributions');
  });
});
