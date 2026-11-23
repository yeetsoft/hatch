import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { SectionNote } from './SectionNote';

describe('SectionNote', () => {
  it('draws a loading line as muted text in a paragraph', () => {
    const html = renderToStaticMarkup(<SectionNote kind="loading">Loading…</SectionNote>);

    expect(html).toBe('<p class="hatch-ambition-note text-muted">Loading…</p>');
  });

  it('draws an empty line as muted text in a paragraph', () => {
    const html = renderToStaticMarkup(<SectionNote kind="empty">No epic is open under this ambition.</SectionNote>);

    expect(html).toBe('<p class="hatch-ambition-note text-muted">No epic is open under this ambition.</p>');
  });

  it('draws a failed line as danger text in a paragraph', () => {
    const html = renderToStaticMarkup(<SectionNote kind="failed">It broke.</SectionNote>);

    expect(html).toBe('<p class="hatch-ambition-note text-danger">It broke.</p>');
  });
});
