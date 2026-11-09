import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ScopeControl } from './ScopeControl';
import type { Project } from '../types';

const noop = () => {};
const one = [{ key: 'HA', name: 'Hatch' }] as Project[];
const three = [
  { key: 'HA', name: 'Hatch' },
  { key: 'AER', name: 'Aerie' },
  { key: 'GL', name: 'glasshole' },
] as Project[];

function render(projects: Project[], keys: string[]) {
  return renderToStaticMarkup(<ScopeControl projects={projects} keys={keys} onChange={noop} />);
}

/** Just the trigger, ahead of the panel - so a check for a key's chip markup
    cannot also match that key's own row further down in the panel, which
    always lists every project. */
function trigger(html: string) {
  return html.slice(0, html.indexOf('hatch-types-panel'));
}

function rows(html: string) {
  return [...html.matchAll(/<label class="hatch-types-row"[\s\S]*?<\/label>/g)].map((m) => m[0]);
}

describe('ScopeControl', () => {
  it('renders nothing for a single-project install', () => {
    expect(render(one, [])).toBe('');
  });

  it('shows All projects, unscoped, on the accent trigger, with no chips', () => {
    const html = trigger(render(three, []));
    expect(html).toContain('All projects');
    expect(html).toContain('hatch-menu__trigger--accent');
    expect(html).not.toContain('hatch-scope-control__chip');
  });

  it('shows one chip for the scoped project and none for the rest', () => {
    const html = trigger(render(three, ['AER']));
    expect([...html.matchAll(/hatch-scope-control__chip/g)]).toHaveLength(1);
    expect(html).toContain('AER');
    expect(html).not.toContain('>HA<');
    expect(html).not.toContain('>GL<');
  });

  it('lists every project in the panel, scoped or not', () => {
    const html = render(three, ['AER']);
    expect(rows(html)).toHaveLength(3);
    expect(html).toContain('<span class="hatch-types-name hatch-scope-control__name">HA — Hatch</span>');
    expect(html).toContain('<span class="hatch-types-name hatch-scope-control__name">AER — Aerie</span>');
    expect(html).toContain('<span class="hatch-types-name hatch-scope-control__name">GL — glasshole</span>');
  });

  it('keeps the trigger and every row under a one-project scope, with only that row ticked', () => {
    const html = render(three, ['HA']);
    expect(html).not.toBe('');
    expect([...trigger(html).matchAll(/hatch-scope-control__chip/g)]).toHaveLength(1);
    const all = rows(html);
    expect(all).toHaveLength(3);
    expect(all.filter((row) => row.includes('checked=""'))).toHaveLength(1);
    expect(all[0]).toContain('checked=""');
  });

  it('never disables a row, for any scope including a single chosen project', () => {
    for (const keys of [[], ['AER'], ['HA', 'AER', 'GL']]) {
      for (const row of rows(render(three, keys))) expect(row).not.toContain('disabled=""');
    }
  });

  it('checks every row when unscoped', () => {
    for (const row of rows(render(three, []))) expect(row).toContain('checked=""');
  });

  it('disables the footer All button only for an empty scope', () => {
    expect(render(three, [])).toContain('<button type="button" class="hatch-filter-clear" disabled="">All</button>');
    expect(render(three, ['AER'])).toContain('<button type="button" class="hatch-filter-clear">All</button>');
  });
});
