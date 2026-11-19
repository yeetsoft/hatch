import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { TypeControl, TypeSteps } from './TypeControl';
import { TypeTile } from './TypeGlyph';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';

const count = (html: string, needle: string) => html.split(needle).length - 1;
/** The ambition's gradient id is per tile and per tree, so ids are taken out of the markup before it is compared. */
const bare = (markup: string) => markup.replace(/hatch-ambition-tile-[^")]+/g, 'id');
const shape = (type: IssueType) => bare(renderToStaticMarkup(<TypeTile type={type} />));

describe('TypeControl', () => {
  it('draws the closed pill with the word, the label and no band', () => {
    const html = renderToStaticMarkup(<TypeControl issueKey="HA-14" type="epic" onChange={() => {}} />);

    expect(html).toContain('epic');
    expect(html).toContain('aria-label="Type: epic. Set HA-14&#x27;s type"');
    expect(html).toContain('title="Type: epic. Set HA-14&#x27;s type"');
    expect(html).toContain('aria-expanded="false"');
    expect(html).toContain('hatch-type-pill hatch-type-pill-press epic');
    expect(html).not.toContain('hatch-type-pill-band');
    expect(html).not.toContain('Set type');
  });

  it('names what it sets when it is given a label', () => {
    const html = renderToStaticMarkup(
      <TypeControl issueKey="HA-14" type="epic" label="Type of the new child" onChange={() => {}} />,
    );

    expect(html).toContain('aria-label="Type: epic. Type of the new child"');
    expect(html).not.toContain('HA-14');
  });

  it.each(ISSUE_TYPES)('wears the %s tile, hidden from the accessible name', (type) => {
    const html = bare(renderToStaticMarkup(<TypeControl issueKey="HA-14" type={type} onChange={() => {}} />));

    expect(html).toContain(`<span class="hatch-type-pill-glyph" aria-hidden="true">${shape(type)}</span>`);
  });

  it('still wears a type the band does not offer', () => {
    const html = renderToStaticMarkup(
      <TypeControl issueKey="HA-14" type="bug" types={['story', 'task']} onChange={() => {}} />,
    );

    expect(html).toContain('Type: bug.');
    expect(html).toContain(shape('bug'));
  });
});

describe('TypeSteps', () => {
  const steps = (current: IssueType, types?: IssueType[]) =>
    bare(renderToStaticMarkup(<TypeSteps current={current} types={types} onPick={() => {}} />));

  it('draws one button per type under a named group, each with its tile and face', () => {
    const html = steps('story');

    expect(html).toContain('role="group"');
    expect(html).toContain('aria-label="Set type"');
    expect(count(html, '<button')).toBe(6);
    for (const type of ISSUE_TYPES) {
      expect(html).toContain(`hatch-type-pill-step ${type}`);
      expect(html).toContain(shape(type));
    }
  });

  it('marks the current type and disables only it', () => {
    const html = steps('task');

    expect(count(html, 'aria-pressed="true"')).toBe(1);
    expect(count(html, 'aria-pressed="false"')).toBe(5);
    expect(count(html, 'disabled=""')).toBe(1);
    expect(html).toMatch(/class="hatch-type-pill-step task here"[^>]*aria-pressed="true"[^>]*disabled=""/);
  });

  it('draws only the types it is given', () => {
    const html = steps('story', ['story', 'task']);

    expect(count(html, '<button')).toBe(2);
    expect(html).not.toContain('hatch-type-pill-step epic');
  });

  it('draws nothing for an empty list', () => {
    expect(steps('story', [])).toBe('');
  });
});
