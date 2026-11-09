import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ChecklistFacet } from './ChecklistFacet';

const noop = () => {};
const options = [
  { value: 'a', text: 'Alpha', count: 0 },
  { value: 'b', text: 'Beta', count: 3 },
  { value: 'c', text: 'Gamma' },
];

function render(chosen: string[]) {
  return renderToStaticMarkup(
    <ChecklistFacet
      label="Things"
      summary="All"
      groupLabel="Things group"
      options={options}
      chosen={chosen}
      onChange={noop}
    />,
  );
}

function rows(html: string) {
  return [...html.matchAll(/<div class="hatch-types-item">[\s\S]*?<\/button><\/div>/g)].map((m) => m[0]);
}

describe('ChecklistFacet', () => {
  it('draws one row per option', () => {
    expect(rows(render(['a', 'b', 'c']))).toHaveLength(options.length);
  });

  it('checks the chosen rows and leaves the rest unchecked', () => {
    const rs = rows(render(['a', 'c']));
    expect(rs[0]).toContain('checked=""');
    expect(rs[1]).not.toContain('checked=""');
    expect(rs[2]).toContain('checked=""');
  });

  it('draws a count when given, 0 included, and no count span when none are given', () => {
    const rs = rows(render(['a', 'b', 'c']));
    expect(rs[0]).toContain('<span class="hatch-types-count">0</span>');
    expect(rs[1]).toContain('<span class="hatch-types-count">3</span>');
    expect(rs[2]).not.toContain('hatch-types-count');
  });

  it('draws no hatch-types-count at all when no option has a count', () => {
    const html = renderToStaticMarkup(
      <ChecklistFacet
        label="Things"
        summary="All"
        groupLabel="Things group"
        options={options.map(({ value, text }) => ({ value, text }))}
        chosen={['a', 'b', 'c']}
        onChange={noop}
      />,
    );
    expect(html).not.toContain('hatch-types-count');
  });

  it('carries the group label as the panel aria-label', () => {
    expect(render(['a', 'b', 'c'])).toContain('role="group" aria-label="Things group"');
  });

  it('holds the summary and the label span in the trigger', () => {
    const html = render(['a', 'b', 'c']);
    expect(html).toContain('<span class="hatch-facet__label">Things</span>All');
  });

  it('is lit only when fewer than every option is chosen', () => {
    expect(render(['a', 'b', 'c'])).not.toContain('hatch-facet--lit');
    expect(render(['a', 'b'])).toContain('hatch-facet--lit');
  });

  it('disables no row when two or more are chosen', () => {
    const rs = rows(render(['a', 'b']));
    for (const r of rs) expect(r).not.toContain('disabled=""');
  });

  it('disables the footer All button only when every option is already chosen', () => {
    expect(render(['a', 'b', 'c'])).toContain('<button type="button" class="hatch-filter-clear" disabled="">All</button>');
    expect(render(['a', 'b'])).toContain('<button type="button" class="hatch-filter-clear">All</button>');
  });

  it('draws one Only button per row, labelled and classed, outside the label', () => {
    const rs = rows(render(['a', 'b', 'c']));
    expect(rs).toHaveLength(options.length);
    for (const [i, r] of rs.entries()) {
      expect(r).toContain(
        `<button type="button" class="hatch-filter-clear hatch-types-only" aria-label="Only ${options[i].text}">Only</button>`,
      );
      const label = r.match(/<label[\s\S]*?<\/label>/)![0];
      expect(label).not.toContain('hatch-types-only');
    }
  });

  it('never disables the Only button, even on the one row left chosen', () => {
    const rs = rows(render(['a']));
    for (const r of rs) {
      const only = r.match(/<button[^>]*hatch-types-only[\s\S]*?<\/button>/)![0];
      expect(only).not.toContain('disabled=""');
    }
  });

  it('draws None beside All, disabled only when nothing is chosen', () => {
    expect(render(['a', 'b'])).toContain('class="hatch-filter-clear">None</button>');
    expect(render([])).toContain('disabled="">None</button>');
  });

  it('leaves All enabled and the trigger lit when nothing is chosen', () => {
    const html = render([]);
    expect(html).toContain('<button type="button" class="hatch-filter-clear">All</button>');
    expect(html).toContain('hatch-facet--lit');
  });

  it('locks no row when a single option is chosen', () => {
    const html = render(['a']);
    for (const r of rows(html)) expect(r).not.toContain('disabled=""');
  });
});
