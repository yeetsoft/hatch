import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { FiledByFacet, filedByChange } from './FiledByFacet';
import { DEFAULT_FILTER } from '../lib/filter';

const names = ['Ada', 'claude-key', 'zoe'];

describe('filedByChange', () => {
  it('collapses every name ticked to null', () => {
    expect(filedByChange({ ...DEFAULT_FILTER, filedBy: ['Ada'] }, names, names).filedBy).toBeNull();
  });

  it('passes a subset, and none, through', () => {
    expect(filedByChange(DEFAULT_FILTER, ['Ada', 'zoe'], names).filedBy).toEqual(['Ada', 'zoe']);
    expect(filedByChange(DEFAULT_FILTER, [], names).filedBy).toEqual([]);
  });
});

describe('FiledByFacet', () => {
  const filers = names.map((name, i) => ({ name, count: i + 1 }));

  it('draws a row and a count per filer, as given', () => {
    const html = renderToStaticMarkup(<FiledByFacet filter={DEFAULT_FILTER} filers={filers} onChange={() => {}} />);
    expect([...html.matchAll(/<span class="hatch-types-name">([^<]*)</g)].map((m) => m[1])).toEqual(names);
    expect(html).toContain('<span class="hatch-types-count">3</span>');
    expect(html).toContain('aria-label="Filed by"');
    expect(html).toContain('<span class="hatch-facet__label">Filed by</span>All');
  });

  it('says None for an empty choice', () => {
    const html = renderToStaticMarkup(
      <FiledByFacet filter={{ ...DEFAULT_FILTER, filedBy: [] }} filers={filers} onChange={() => {}} />,
    );
    expect(html).toContain('<span class="hatch-facet__label">Filed by</span>None');
  });
});
