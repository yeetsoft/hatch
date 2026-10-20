import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypeGlyph } from './TypeGlyph';
import type { IssueType } from '../types';

describe('TypeGlyph', () => {
  it.each<IssueType>(['epic', 'reflection', 'story', 'task', 'bug'])(
    'names %s on the title and the accessible name, and wears its own class',
    (type) => {
      const html = renderToStaticMarkup(<TypeGlyph type={type} />);

      expect(html).toContain(`title="${type}"`);
      expect(html).toContain(`aria-label="${type}"`);
      expect(html).toContain(`hatch-type-glyph--${type}`);
    },
  );

  it('draws five different shapes, not five colours of the same one', () => {
    const shapes = {
      epic: renderToStaticMarkup(<TypeGlyph type="epic" />),
      reflection: renderToStaticMarkup(<TypeGlyph type="reflection" />),
      story: renderToStaticMarkup(<TypeGlyph type="story" />),
      task: renderToStaticMarkup(<TypeGlyph type="task" />),
      bug: renderToStaticMarkup(<TypeGlyph type="bug" />),
    };

    expect(shapes.story).toContain('<circle');
    expect(shapes.bug).toContain('<polygon');
    expect(shapes.epic).toContain('transform="rotate(45 8 8)"');
    expect(shapes.task).not.toContain('transform=');
    expect(shapes.epic).toContain('<rect');
    expect(shapes.task).toContain('<rect');

    expect(shapes.epic).not.toContain('<circle');
    expect(shapes.epic).not.toContain('<polygon');
    expect(shapes.story).not.toContain('<rect');
    expect(shapes.story).not.toContain('<polygon');
    expect(shapes.task).not.toContain('<circle');
    expect(shapes.task).not.toContain('<polygon');
    expect(shapes.bug).not.toContain('<rect');
    expect(shapes.bug).not.toContain('<circle');

    // The hexagon and the triangle are both a <polygon>, so the tag cannot
    // tell them apart - the number of points does.
    const pointCount = (svg: string) => /points="([^"]+)"/.exec(svg)![1].trim().split(/\s+/).length;
    expect(shapes.reflection).toContain('<polygon');
    expect(pointCount(shapes.reflection)).toBe(6);
    expect(pointCount(shapes.bug)).toBe(3);
    expect(shapes.reflection).not.toContain('<circle');
    expect(shapes.reflection).not.toContain('<rect');
    expect(shapes.epic).not.toContain('points=');
    expect(shapes.story).not.toContain('points=');
    expect(shapes.task).not.toContain('points=');
  });
});
