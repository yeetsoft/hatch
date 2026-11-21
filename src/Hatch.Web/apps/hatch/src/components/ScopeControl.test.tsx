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
    expect(html.match(/<button[^>]*>/)?.[0] ?? '').toContain('hatch-scope-control--unscoped');
  });

  it('keeps the pill off the scoped trigger, which wears its chips', () => {
    const button = trigger(render(three, ['AER'])).match(/<button[^>]*>/)?.[0] ?? '';
    expect(button).toContain('hatch-scope-control--scoped');
    expect(button).not.toContain('hatch-scope-control--unscoped');
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

  describe('the cluster of expedited and paused projects', () => {
    const level = (priority: string, canAdminister: boolean) =>
      [
        { key: 'HA', name: 'Hatch', priority: priority === 'mixed' ? 'expedited' : priority, canAdminister, color: '#112233' },
        { key: 'AER', name: 'Aerie', priority: priority === 'mixed' ? 'paused' : priority, canAdminister, color: '#445566' },
        { key: 'GL', name: 'glasshole', priority: 'normal', canAdminister },
      ] as Project[];

    /** What follows the trigger's own closing tag and ahead of the panel's. */
    const cluster = (html: string) => html.match(/<div class="hatch-scope-cluster">[\s\S]*$/)?.[0] ?? '';

    it('draws nothing, not an empty box, when no project is expedited or paused', () => {
      const html = render(three, []);
      expect(html).not.toContain('hatch-scope-cluster');
      expect(html).not.toContain('hatch-scope-control__trigger-glyph');
    });

    it('draws a glyph and one pill for a single expedited project', () => {
      const html = cluster(render([{ ...three[0], priority: 'expedited' }, ...three.slice(1)] as Project[], []));
      expect(html).toContain('title="Expedited projects: HA"');
      expect(html).toContain('hatch-scope-cluster__group--expedited');
      expect(html).not.toContain('hatch-scope-cluster__group--paused');
      expect([...html.matchAll(/hatch-scope-cluster__pill"/g)]).toHaveLength(1);
    });

    it('draws a run for each level, in the list order, titled with its keys', () => {
      const projects = [
        { key: 'A', name: 'Alpha', priority: 'expedited' },
        { key: 'B', name: 'Beta', priority: 'paused' },
        { key: 'C', name: 'Gamma', priority: 'expedited' },
        { key: 'D', name: 'Delta', priority: 'normal' },
      ] as Project[];
      const html = cluster(render(projects, []));
      expect(html).toContain('title="Expedited projects: A, C"');
      expect(html).toContain('title="Paused projects: B"');
      expect(html.indexOf('Expedited projects')).toBeLessThan(html.indexOf('Paused projects'));
      expect(html.indexOf('>A')).toBeLessThan(html.indexOf('>C'));
      expect(html).not.toContain('>D');
    });

    it('puts the first pill on top and tints each in its project colour', () => {
      const html = cluster(render(level('expedited', false), []));
      const pills = [...html.matchAll(/<span[^>]*hatch-scope-cluster__pill"[^>]*>/g)].map((m) => m[0]);
      expect(pills).toHaveLength(2);
      expect(pills[0]).toContain('z-index:2');
      expect(pills[1]).toContain('z-index:1');
      expect(pills[0]).toContain('--mark-color');
      expect(pills[0]).toContain('#112233');
      expect(pills[1]).toContain('#445566');
    });

    it('gives an owner an x on each pill, labelled to set the project back to normal', () => {
      const html = cluster(render(level('mixed', true), []));
      expect(html).toContain('aria-label="Set HA back to normal"');
      expect(html).toContain('aria-label="Set AER back to normal"');
      expect(html).not.toContain('aria-label="Set GL back to normal"');
      expect(html).not.toContain('title="Hatch"');
    });

    it('gives a non-owner plain pills titled with the project name, and no x', () => {
      const html = cluster(render(level('mixed', false), []));
      expect(html).not.toContain('<button');
      expect(html).not.toContain('back to normal');
      expect(html).toContain('title="Hatch"');
      expect(html).toContain('title="Aerie"');
    });

    it('draws the cluster after the trigger button, outside it', () => {
      const html = render(level('mixed', true), ['GL']);
      const ahead = html.slice(0, html.indexOf('hatch-types-panel'));
      expect(ahead).not.toContain('hatch-scope-cluster');
      expect(html.indexOf('hatch-scope-cluster')).toBeGreaterThan(html.indexOf('hatch-types-panel'));
      expect(html.match(/^<div class="hatch-scope-control">/)).not.toBeNull();
    });

    it.each([[[]], [['GL']]])('rides the glyphs in the trigger, before the caret, for scope %j', (keys) => {
      const ahead = trigger(render(level('mixed', false), keys));
      const glyphs = [...ahead.matchAll(/<span class="hatch-scope-control__trigger-glyph[^"]*" aria-hidden="true">/g)];
      expect(glyphs).toHaveLength(2);
      expect(glyphs[1].index!).toBeLessThan(ahead.indexOf('hatch-menu__caret'));
      expect(ahead.match(/<span class="hatch-scope-control__trigger-glyph[^>]*>/g)?.join('')).not.toContain('title=');
    });

    it('rides only the glyph of the level present', () => {
      const ahead = trigger(render(level('paused', false), []));
      expect(ahead).toContain('trigger-glyph--pause');
      expect(ahead).not.toContain('trigger-glyph--expedite');
    });
  });
});
