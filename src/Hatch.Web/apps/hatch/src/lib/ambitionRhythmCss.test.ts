/* The ambition page's sections share one list and one row. There is no DOM in
   this runner to draw them in, so App.css and IssuePage.tsx are read as text,
   the way ambitionTokensCss.test.ts reads App.css. */
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../App.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
const page = readFileSync(new URL('../pages/IssuePage.tsx', import.meta.url), 'utf8');

/** Every rule body whose selector list holds exactly this selector. */
function bodiesOf(selector: string): string[] {
  const out: string[] = [];
  for (const [, list, body] of css.matchAll(/([^{}@;]+)\{([^{}]*)\}/g)) {
    if (list.split(',').map((s) => s.trim()).includes(selector)) out.push(body);
  }
  return out;
}

/** The markup of a component or function, from its declaration to the next top-level one. */
function sourceOf(name: string): string {
  const start = page.search(new RegExp(`^(?:export )?function ${name}\\b`, 'm'));
  expect(start, `${name} is in IssuePage.tsx`).toBeGreaterThanOrEqual(0);
  const rest = page.slice(start + 1);
  const next = rest.search(/^(?:export )?function /m);
  return next === -1 ? rest : rest.slice(0, next);
}

describe('one list rule', () => {
  it.each(['.hatch-capability-list', '.hatch-change-entries', '.hatch-pass-list'])('has no copy as %s', (selector) => {
    expect(bodiesOf(selector)).toEqual([]);
    expect(page).not.toContain(selector.slice(1));
  });
});

describe('one row rule', () => {
  it.each(['.hatch-capability-row', '.hatch-change-entry', '.hatch-pass'])(
    '%s declares no padding, rule or flex of its own',
    (selector) => {
      for (const body of bodiesOf(selector)) {
        expect(body).not.toMatch(/(?:^|[;\s])(?:padding|border-top|display|flex-wrap|gap)\s*:/);
      }
    },
  );

  it.each([
    ['CapabilityRow', 'hatch-capability-row'],
    ['PassEntry', 'hatch-pass'],
    ['ChangeEntryLine', 'hatch-change-entry'],
    ['ChangeOutcomeLine', 'hatch-change-outcome'],
  ])('%s draws its row as .hatch-progress-row', (fn, rowClass) => {
    expect(sourceOf(fn)).toContain(`className="hatch-progress-row ${rowClass}"`);
  });

  it.each(['Capabilities', 'Passes', 'EpicsInFlight', 'LatestChange', 'Serves'])(
    "%s's list is .hatch-progress-list",
    (fn) => {
      const source = sourceOf(fn);
      const lists = [...source.matchAll(/<ul className="([^"]*)"/g)].map((m) => m[1]);
      expect(lists.length).toBeGreaterThan(0);
      for (const list of lists) expect(list).toBe('hatch-progress-list');
    },
  );
});

describe('the gap under a heading', () => {
  it('is one rule on the ambition section', () => {
    expect(bodiesOf('.hatch-ambition-section > summary + *')).toHaveLength(1);
  });

  it.each(['Capabilities', 'Budget', 'LatestChange', 'EpicsInFlight', 'Passes', 'Serves'])(
    '%s opts in',
    (fn) => {
      expect(sourceOf(fn)).toContain('className="hatch-ambition-section"');
    },
  );
});
