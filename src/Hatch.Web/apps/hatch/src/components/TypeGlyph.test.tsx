import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypeGlyph, TypeTile } from './TypeGlyph';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';

const tile = (type: IssueType) => renderToStaticMarkup(<TypeTile type={type} />);

/** The markup inside the scaled group: the picture, with no outline and no type colour in it. */
const picture = (type: IssueType) => /<g [^>]*>(.*)<\/g>/.exec(tile(type))![1];

describe('TypeGlyph', () => {
  it.each<IssueType>(ISSUE_TYPES)(
    'names %s on the title and the accessible name, and wears its own class',
    (type) => {
      const html = renderToStaticMarkup(<TypeGlyph type={type} />);

      expect(html).toContain(`title="${type}"`);
      expect(html).toContain(`aria-label="${type}"`);
      expect(html).toContain(`hatch-type-glyph--${type}`);
    },
  );
});

describe('TypeTile', () => {
  it.each<IssueType>(ISSUE_TYPES)('fills the %s tile with its own token', (type) => {
    const html = tile(type);

    expect(html).toContain('class="hatch-type-tile-face"');
    expect(html).toContain(`style="fill:var(--type-${type})"`);
    expect(html).toContain('aria-hidden="true"');
    expect(html).toContain('focusable="false"');
  });

  it('draws a disc for bug and reflection only', () => {
    for (const type of ISSUE_TYPES) {
      const disc = type === 'bug' || type === 'reflection';
      expect(tile(type).includes('<circle'), type).toBe(disc);
    }
  });

  it('scales the picture to 110% about the middle of the box', () => {
    for (const type of ISSUE_TYPES) {
      expect(tile(type)).toContain('<g transform="translate(8 8) scale(1.1) translate(-8 -8)">');
    }
  });

  it('draws a different picture for every type', () => {
    const pictures = ISSUE_TYPES.map(picture);

    expect(pictures.every((markup) => markup.length > 0)).toBe(true);
    expect(new Set(pictures).size).toBe(ISSUE_TYPES.length);
  });

  it('strokes the task tick 1.9 wide and fills the story bookmark', () => {
    expect(picture('task')).toContain('stroke-width="1.9"');
    expect(picture('task')).not.toContain('hatch-type-tile-picture');
    expect(picture('story')).toContain('hatch-type-tile-picture');
    expect(picture('story')).not.toContain('hatch-type-tile-stroke');
  });
});
