import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { HATCH_MARK_BODY, HATCH_MARK_CAP } from '@hatch/ui';
import { AmbitionTile, TypeGlyph, TypeTile } from './TypeGlyph';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';

const tile = (type: IssueType) => renderToStaticMarkup(<TypeTile type={type} />);

/** The ambition's fill is a per-tile gradient, not a `--type-*` token, and its egg is not scaled by 110%. */
const TOKENED = ISSUE_TYPES.filter((type) => type !== 'ambition');

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
  it.each<IssueType>(TOKENED)('fills the %s tile with its own token', (type) => {
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
    for (const type of TOKENED) {
      expect(tile(type)).toContain('<g transform="translate(8 8) scale(1.1) translate(-8 -8)">');
    }
  });

  it('draws a different picture for every type', () => {
    const pictures = ISSUE_TYPES.map(picture);

    expect(pictures.every((markup) => markup.length > 0)).toBe(true);
    expect(new Set(pictures).size).toBe(ISSUE_TYPES.length);
  });

  it('draws the ambition as the egg on a gradient, and the six tiles all differ', () => {
    const ambition = tile('ambition');

    expect(ambition).toBe(renderToStaticMarkup(<AmbitionTile />));
    expect(ambition).toContain('fill:url(#');
    expect(ambition).not.toContain('--type-ambition');
    expect(ambition).toContain(HATCH_MARK_BODY);
    expect(new Set(ISSUE_TYPES.map(tile)).size).toBe(ISSUE_TYPES.length);
  });

  it('strokes the task tick 1.9 wide and fills the story bookmark', () => {
    expect(picture('task')).toContain('stroke-width="1.9"');
    expect(picture('task')).not.toContain('hatch-type-tile-picture');
    expect(picture('story')).toContain('hatch-type-tile-picture');
    expect(picture('story')).not.toContain('hatch-type-tile-stroke');
  });
});

describe('AmbitionTile', () => {
  const html = renderToStaticMarkup(<AmbitionTile />);
  const paths = [...html.matchAll(/<path [^>]*>/g)].map((match) => match[0]);
  const dOf = (path: string) => /\sd="([^"]*)"/.exec(path)![1];

  it("draws the egg from the mark's two paths, not a copy", () => {
    const egg = paths.filter((path) => path.includes('hatch-type-tile-picture')).map(dOf);

    expect(egg).toEqual([HATCH_MARK_CAP, HATCH_MARK_BODY]);
  });

  it('wears the same rounded square as the other sizes of work', () => {
    const face = paths.find((path) => path.includes('hatch-type-tile-face'))!;

    expect(dOf(face)).toBe(dOf(/<path [^>]*hatch-type-tile-face[^>]*>/.exec(tile('epic'))![0]));
  });

  it('keeps the egg at its final size, centred on the silhouette', () => {
    expect(html).toContain('<g transform="translate(2.0500 1.9905) scale(0.119)">');
    expect(html).not.toContain('1.1');
  });

  it('gives each tile its own gradient, and points its face at it', () => {
    const two = renderToStaticMarkup(
      <>
        <AmbitionTile />
        <AmbitionTile />
      </>,
    );
    const ids = [...two.matchAll(/<linearGradient id="([^"]+)"/g)].map((match) => match[1]);
    const fills = [...two.matchAll(/fill:url\(#([^)]+)\)/g)].map((match) => match[1]);

    expect(new Set(ids).size).toBe(2);
    expect(fills).toEqual(ids);
  });

  it('reads both gradient tokens in its stops and holds no colour literal', () => {
    expect(html).toContain('style="stop-color:var(--gradient-ink-from)"');
    expect(html).toContain('style="stop-color:var(--gradient-ink-to)"');
    expect(html).not.toMatch(/#[0-9a-f]{3,8}/i);
    expect(html).not.toMatch(/rgb|hsl/i);
  });

  it('runs the gradient from top left to bottom right over the tile', () => {
    expect(html).toContain('gradientUnits="userSpaceOnUse" x1="1" y1="1" x2="15" y2="15"');
  });
});
