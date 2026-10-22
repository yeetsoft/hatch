import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { RunnerTagsField } from './RunnerTagsField';

describe('RunnerTagsField', () => {
  it('renders the label, hint and value, one tag per line', () => {
    const html = renderToStaticMarkup(
      <RunnerTagsField label="Runner tags" hint="One tag per line." value={'macos\nxcode'} onChange={() => {}} />,
    );

    expect(html).toContain('<span class="hatch-field__label">Runner tags</span>');
    expect(html).toContain('<span class="hatch-field__hint">One tag per line.</span>');
    expect(html).toContain('macos\nxcode');
  });

  it('carries rows="2" and the hatch-grows class', () => {
    const html = renderToStaticMarkup(
      <RunnerTagsField label="Runner tags" hint="One tag per line." value="" onChange={() => {}} />,
    );

    expect(html).toMatch(/<textarea[^>]*rows="2"[^>]*class="hatch-grows"/);
  });

  it('disables the textarea when disabled is set', () => {
    const html = renderToStaticMarkup(
      <RunnerTagsField label="Runner tags" hint="One tag per line." value="" onChange={() => {}} disabled />,
    );

    expect(html).toMatch(/<textarea[^>]*disabled=""/);
  });

  it('disables the textarea when busy is set', () => {
    const html = renderToStaticMarkup(
      <RunnerTagsField label="Runner tags" hint="One tag per line." value="" onChange={() => {}} busy />,
    );

    expect(html).toMatch(/<textarea[^>]*disabled=""/);
  });
});
