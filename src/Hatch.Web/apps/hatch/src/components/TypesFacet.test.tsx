import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypesFacet } from './TypesFacet';
import { DEFAULT_FILTER } from '../lib/filter';
import { ISSUE_TYPES } from '../types';

const noop = () => {};

function render(types = ISSUE_TYPES) {
  const counts = { epic: 0, reflection: 3, story: 5, task: 0, bug: 2 };
  return renderToStaticMarkup(
    <TypesFacet filter={{ ...DEFAULT_FILTER, types }} counts={counts as never} onChange={noop} />,
  );
}

describe('TypesFacet', () => {
  it('draws the five issue types in ISSUE_TYPES order', () => {
    const html = render();
    const names = [...html.matchAll(/<span class="hatch-types-name">([^<]*)</g)].map((m) => m[1]);
    expect(names).toEqual(ISSUE_TYPES);
  });

  it('draws a 0 count', () => {
    expect(render()).toContain('<span class="hatch-types-count">0</span>');
  });

  it('labels the panel Issue types', () => {
    expect(render()).toContain('aria-label="Issue types"');
  });

  it('summarises four of five types as All but tasks', () => {
    const types = ISSUE_TYPES.filter((t) => t !== 'task');
    expect(render(types)).toContain('<span class="hatch-facet__label">Types</span>All but tasks');
  });

  it('locks the last type with Keep at least one type', () => {
    const html = render(['epic']);
    expect(html).toContain('title="Keep at least one type"');
  });
});
