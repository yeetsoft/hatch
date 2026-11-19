import { describe, expect, it } from 'vitest';
import type { ComponentProps } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { HATCH_MARK_BODY, HATCH_MARK_CAP } from '@hatch/ui';
import { AmbitionTile, TypeGlyph, TypeTile } from './TypeGlyph';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';
import { HEATS, LEAN, STREAMING, TRAILS } from '../lib/tileMarks';
import type { Priority } from '../lib/tileMarks';

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

describe('TypeTile marks', () => {
  const marked = (type: IssueType, props: Partial<ComponentProps<typeof TypeTile>> = {}) =>
    renderToStaticMarkup(<TypeTile type={type} {...props} />);
  const trailOf = (html: string) => /class="hatch-type-tile-trail" d="([^"]*)"/.exec(html)?.[1];
  const lines = (d: string | undefined) => (d ? d.split('M').length - 1 : 0);
  const faceOf = (html: string) => /<(?:path|circle) class="hatch-type-tile-face"[^>]*>/.exec(html)![0];

  /* The ambition draws no marks yet: it returns its own tile before they are read. */
  describe.each<IssueType>(TOKENED)('%s', (type) => {
    it('draws the same bare, with no props and with every default given', () => {
      const bare = tile(type);

      expect(marked(type, { preApproval: 'none', priority: 'normal', fill: 'solid' })).toBe(bare);
      expect(bare).toContain('viewBox="0 0 16 16"');
      for (const mark of ['--heat', 'trail', 'lean', 'landed', 'overflow', 'opacity', 'style="width']) {
        expect(bare, mark).not.toContain(mark);
      }
      expect(bare.match(/<g /g)).toHaveLength(1);
    });

    it('draws each pre-approval as its row says', () => {
      expect(trailOf(marked(type, { preApproval: 'none' }))).toBeUndefined();
      expect(trailOf(marked(type, { preApproval: 'express' }))).toBe(TRAILS.express.lines);
      expect(trailOf(marked(type, { preApproval: 'preapproved' }))).toBe(TRAILS.preapproved.lines);
      expect(lines(trailOf(marked(type, { preApproval: 'express' })))).toBe(1);
      expect(lines(trailOf(marked(type, { preApproval: 'preapproved' })))).toBe(3);
      expect(trailOf(marked(type, { preApproval: 'express' }))).toBe('M-7.6 8h5.2');
    });

    it('draws the trail in the type colour at 65%, 1.3 wide with round caps', () => {
      const html = marked(type, { preApproval: 'express' });

      expect(html).toContain(
        `style="fill:none;stroke:var(--type-${type});stroke-opacity:0.65;stroke-width:1.3;stroke-linecap:round"`,
      );
    });

    it('leans for pre-approved alone, with no flame', () => {
      const html = marked(type, { preApproval: 'preapproved' });

      expect(html).toContain(`transform="${LEAN}"`);
      expect(html).toContain('translate(1.5 0) skewX(-10)');
      expect(html).not.toContain('--heat');
      expect(marked(type, { preApproval: 'express' })).not.toContain('hatch-type-tile-lean');
    });

    it.each<Priority>(['paused', 'low', 'normal'])('draws nothing for %s', (priority) => {
      const html = marked(type, { priority });

      expect(html).toBe(tile(type));
    });

    it('fades economy to 40% with no flame, in the 16 box', () => {
      const html = marked(type, { priority: 'economy' });

      expect(html).toContain('opacity="0.4"');
      expect(html).not.toContain('--heat');
      expect(html).toContain('viewBox="0 0 16 16"');
      expect(faceOf(html)).not.toContain('stroke');
    });

    it('draws an ember for expedited and the upright flame for emergency, behind the face', () => {
      for (const priority of ['expedited', 'emergency'] as const) {
        const html = marked(type, { priority });

        expect(html).toContain(`<g transform="${HEATS[priority].transform}"`);
        expect(html).toContain('fill:var(--heat)');
        expect(html).toContain('fill:var(--heat-core)');
        expect(html.indexOf('hatch-type-tile-flame')).toBeLessThan(html.indexOf('hatch-type-tile-face'));
        expect(html).toContain('viewBox="-12 -8 29 25"');
      }
    });

    it('draws the core as the body again at 0.52 about the base, sharing it', () => {
      const html = marked(type, { priority: 'emergency' });
      const bodies = [...html.matchAll(/<path d="([^"]*)"( transform="([^"]*)")? style="fill:var\(--heat(-core)?\)"/g)];

      expect(bodies).toHaveLength(2);
      expect(bodies[0][1]).toBe(bodies[1][1]);
      expect(bodies[1][3]).toBe('translate(12 22) scale(0.52) translate(-12 -22)');
    });

    it('outlines the face in the ground colour only where a flame is drawn', () => {
      const ground = 'stroke:var(--tile-ground, var(--bg));stroke-width:1.8;paint-order:stroke';

      expect(faceOf(marked(type, { priority: 'expedited' }))).toContain(ground);
      expect(faceOf(marked(type, { priority: 'emergency' }))).toContain(ground);
      expect(faceOf(marked(type, { priority: 'economy' }))).not.toContain('stroke');
      expect(faceOf(marked(type, { preApproval: 'express' }))).not.toContain('stroke');
    });

    it('streams the flame back over two lines for a pre-approved emergency', () => {
      const html = marked(type, { preApproval: 'preapproved', priority: 'emergency' });

      expect(html).toContain(`<g transform="${STREAMING.transform}"`);
      expect(html).toContain('rotate(-74)');
      expect(trailOf(html)).toBe(STREAMING.lines);
      expect(trailOf(html)).toBe('M-9.5 12.6h6.4 M-6.4 15.2h3.6');
      expect(lines(trailOf(html))).toBe(2);
      expect(html).toContain(`transform="${LEAN}"`);
    });

    it('draws any mix of trail, heat and fill at once', () => {
      const html = marked(type, { preApproval: 'express', priority: 'expedited', fill: 'landed' });

      expect(trailOf(html)).toBe(TRAILS.express.lines);
      expect(html).toContain('fill:var(--heat)');
      expect(html).toContain('hatch-type-tile-landed');
      expect(html).toContain('viewBox="-12 -8 29 25"');
    });

    it('draws the three fills', () => {
      const outline = marked(type, { fill: 'outline' });
      const landed = marked(type, { fill: 'landed' });

      expect(faceOf(outline)).toContain(`fill:none;stroke:var(--type-${type});stroke-width:1.2`);
      expect(outline).toContain('class="hatch-type-tile-outlined"');
      expect(outline).toContain(`style="color:var(--type-${type})"`);
      expect(marked(type, { fill: 'solid' })).toBe(tile(type));
      expect(faceOf(landed)).toContain(`fill:var(--type-${type})`);
      expect(landed).toContain('cx="13.6" cy="13.2" r="3.6"');
      expect(landed).toContain('fill:var(--success-fill);stroke:var(--tile-ground, var(--bg));stroke-width:1.2');
      expect(landed).toContain('d="M12 13.3 13.2 14.5 15.3 12.1"');
      expect(landed).toContain('stroke:var(--on-accent);stroke-width:1.3;stroke-linecap:round');
    });

    it('backs an outlined tile with the ground where a flame is behind it', () => {
      const html = marked(type, { fill: 'outline', priority: 'emergency' });

      expect(html).toContain('style="fill:var(--tile-ground, var(--bg));stroke:var(--tile-ground, var(--bg));stroke-width:1.8"');
      expect(marked(type, { fill: 'outline' })).not.toContain('fill:var(--tile-ground');
    });
  });

  it('draws the landed disc and ring whole, past both boxes, never scaled down', () => {
    const html = marked('task', { fill: 'landed' });
    const reachX = 13.6 + 3.6 + 1.2 / 2;
    const reachY = 13.2 + 3.6 + 1.2 / 2;

    expect(reachX).toBeCloseTo(17.8);
    expect(reachY).toBeCloseTo(17.4);
    expect(html).toContain('overflow="visible"');
    expect(html).toContain('viewBox="0 0 16 16"');
    expect(marked('task', { fill: 'solid' })).not.toContain('overflow');
  });

  it('sizes the wide box so the 16 box inside is the size the wrapper asks for', () => {
    const html = marked('story', { priority: 'emergency' });

    expect(html).toContain('width:181.25%;height:156.25%;max-width:none;margin-left:-75%;margin-top:-50%');
    expect(html).toContain('overflow:visible');
    expect(marked('story', { priority: 'economy' })).not.toContain('width:');
  });

  it('reads the ground from --tile-ground and falls back to --bg', () => {
    expect(marked('epic', { priority: 'expedited' })).toContain('var(--tile-ground, var(--bg))');
  });

  it('holds no colour literal in any mark', () => {
    const everything = marked('epic', { preApproval: 'preapproved', priority: 'emergency', fill: 'landed' });

    expect(everything).not.toMatch(/#[0-9a-f]{3,8}/i);
    expect(everything).not.toMatch(/rgb|hsl/i);
  });
});

describe('TypeGlyph, given a card\'s levels', () => {
  it('hands priority and pre-approval to the tile, and names itself from the title it is given', () => {
    const html = renderToStaticMarkup(
      <TypeGlyph type="bug" priority="emergency" preApproval="preapproved" title="bug; a sentence" />,
    );

    expect(html).toContain('title="bug; a sentence" aria-label="bug; a sentence"');
    expect(html).toContain('fill:var(--heat)');
    expect(html).toContain('hatch-type-tile-trail');
  });

  it('draws the bare tile when given neither', () => {
    const html = renderToStaticMarkup(<TypeGlyph type="bug" />);

    expect(html).toContain('title="bug" aria-label="bug"');
    expect(html).not.toContain('var(--heat)');
    expect(html).not.toContain('hatch-type-tile-trail');
  });
});
