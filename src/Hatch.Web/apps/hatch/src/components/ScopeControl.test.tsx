import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ScopeControl } from './ScopeControl';
import type { Project } from '../types';

const noop = () => {};
const one = [{ key: 'HA', name: 'Hatch' }] as Project[];
const three = [
  { key: 'HA', name: 'Hatch', priority: 'normal' },
  { key: 'AER', name: 'Aerie', priority: 'normal' },
  { key: 'GL', name: 'glasshole', priority: 'normal' },
] as Project[];

function render(projects: Project[], keys: string[]) {
  return renderToStaticMarkup(<ScopeControl projects={projects} keys={keys} onChange={noop} onApply={noop} />);
}

/** Just the trigger, ahead of the panel - so a check for a key's chip markup
    cannot also match that key's own row further down in the panel, which
    always lists every project. */
function trigger(html: string) {
  return html.slice(0, html.indexOf('hatch-types-panel'));
}

function rows(html: string) {
  return [...html.matchAll(/<div class="hatch-types-item">[\s\S]*?<\/button><\/div>/g)].map((m) => m[0]);
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
    expect(html).toContain('hatch-menu__caret');
  });

  it('shows one chip for the scoped project and none for the rest', () => {
    const html = trigger(render(three, ['AER']));
    expect(html).toContain('hatch-menu__caret');
    expect([...html.matchAll(/hatch-scope-control__chip/g)]).toHaveLength(1);
    expect(html).toContain('AER');
    expect(html).not.toContain('>HA<');
    expect(html).not.toContain('>GL<');
  });

  it('puts the hint on the scoped trigger itself, in the words its label carries, and on no chip', () => {
    const html = trigger(render(three, ['HA', 'AER']));
    const button = html.match(/<button[^>]*>/)?.[0] ?? '';
    expect(button).toContain('title="Projects in scope: Hatch, Aerie"');
    expect(button).toContain('aria-label="Projects in scope: Hatch, Aerie"');
    expect(html.match(/<span class="hatch-scope-control__chip"[^>]*>/g)?.join('') ?? '').not.toContain('title=');
    expect(html).not.toContain('title="Hatch"');
  });

  it('gives the unscoped trigger no title, since its text says it all', () => {
    const button = trigger(render(three, [])).match(/<button[^>]*>/)?.[0] ?? '';
    expect(button).not.toContain('title=');
  });

  it('draws no project mark inside a scoped chip', () => {
    const html = trigger(render(three, ['AER']));
    expect(html).toContain('hatch-scope-control__chip');
    expect(html).not.toContain('hatch-project-mark');
    expect(html).toMatch(/<span class="hatch-scope-control__chip"[^>]*>AER<\/span>/);
  });

  it('keeps an icon project out of the scoped chip but in its panel row', () => {
    const html = render([{ key: 'HA', name: 'Hatch', icon: 'rocket' }, ...three.slice(1)] as Project[], ['HA']);
    const chip = trigger(html).match(/<span class="hatch-scope-control__chip"[^>]*>[\s\S]*?<\/span>/)?.[0] ?? '';
    expect(chip).toContain('>HA</span>');
    expect(chip).not.toContain('<svg');
    expect(rows(html)[0]).toContain('<svg');
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

  it('draws one Only button per row, labelled and classed, outside the label', () => {
    const rs = rows(render(three, []));
    expect(rs).toHaveLength(3);
    for (const [i, r] of rs.entries()) {
      expect(r).toContain(
        `<button type="button" class="hatch-filter-clear hatch-types-only" aria-label="Only ${three[i].key} — ${three[i].name}">Only</button>`,
      );
      const label = r.match(/<label[\s\S]*?<\/label>/)![0];
      expect(label).not.toContain('hatch-types-only');
    }
  });

  it('never disables the Only button, for any scope including a single chosen project', () => {
    for (const keys of [[], ['AER'], ['HA', 'AER', 'GL']]) {
      for (const r of rows(render(three, keys))) {
        const only = r.match(/<button[^>]*hatch-types-only[\s\S]*?<\/button>/)![0];
        expect(only).not.toContain('disabled=""');
      }
    }
  });

  it('draws no global None beside All, since None would just mean every project again', () => {
    const html = render(three, ['AER']);
    expect(html).not.toContain('>None<');
  });

  it('gives an owner an expedite arrow and a pause button, between the name and Only', () => {
    const owner = [{ key: 'HA', name: 'Hatch', priority: 'normal', canAdminister: true }, ...three.slice(1)] as Project[];
    const row = rows(render(owner, []))[0];
    const label = row.match(/<label[\s\S]*?<\/label>/)![0];
    const afterLabel = row.slice(row.indexOf(label) + label.length);
    const onlyIndex = afterLabel.indexOf('hatch-types-only');
    expect(afterLabel.indexOf('hatch-scope-control__level--expedite')).toBeGreaterThan(-1);
    expect(afterLabel.indexOf('hatch-scope-control__level--pause')).toBeGreaterThan(-1);
    expect(afterLabel.indexOf('hatch-scope-control__level--expedite')).toBeLessThan(onlyIndex);
    expect(afterLabel.indexOf('hatch-scope-control__level--pause')).toBeLessThan(onlyIndex);
  });

  it.each(['normal', 'expedited', 'paused'] as const)(
    'shows the right pair of glyph states for an owner at %s',
    (priority) => {
      const owner = [{ key: 'HA', name: 'Hatch', priority, canAdminister: true }, ...three.slice(1)] as Project[];
      const row = rows(render(owner, []))[0];
      const arrow = row.match(/<button[^>]*hatch-scope-control__level--expedite[^>]*>[\s\S]*?<\/button>/)![0];
      const pause = row.match(/<button[^>]*hatch-scope-control__level--pause[^>]*>[\s\S]*?<\/button>/)![0];

      expect(arrow).toContain(priority === 'expedited' ? 'aria-pressed="true"' : 'aria-pressed="false"');
      expect(pause).toContain(priority === 'paused' ? 'aria-pressed="true"' : 'aria-pressed="false"');
      expect(arrow).toContain(priority === 'expedited' ? 'Stop expediting HA — Hatch' : 'Expedite HA — Hatch');
      expect(pause).toContain(priority === 'paused' ? 'Resume HA — Hatch' : 'Pause HA — Hatch');
    },
  );

  it('draws no level button for a non-owner, marking only a project already at a level', () => {
    const viewer = [
      { key: 'HA', name: 'Hatch', priority: 'normal', canAdminister: false },
      { key: 'AER', name: 'Aerie', priority: 'expedited', canAdminister: false },
      { key: 'GL', name: 'glasshole', priority: 'paused', canAdminister: false },
    ] as Project[];
    const rs = rows(render(viewer, []));

    for (const row of rs) expect(row).not.toContain('hatch-scope-control__level ');

    expect(rs[0]).not.toContain('hatch-scope-control__level-mark');
    expect(rs[1]).toContain('title="Expedited"');
    expect(rs[1]).toContain('aria-label="Expedited"');
    expect(rs[2]).toContain('title="Paused"');
    expect(rs[2]).toContain('aria-label="Paused"');
  });
});
