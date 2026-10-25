import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { RunnerProjectsField } from './RunnerProjectsField';

describe('RunnerProjectsField', () => {
  it('renders the label, hint and value, one project key per line', () => {
    const html = renderToStaticMarkup(
      <RunnerProjectsField label="Projects" hint="One project key per line." value={'HA\nAER'} onChange={() => {}} />,
    );

    expect(html).toContain('<span class="hatch-field__label">Projects</span>');
    expect(html).toContain('<span class="hatch-field__hint">One project key per line.</span>');
    expect(html).toContain('HA\nAER');
  });

  it('carries rows="2" and the hatch-grows class', () => {
    const html = renderToStaticMarkup(<RunnerProjectsField label="Projects" value="" onChange={() => {}} />);

    expect(html).toMatch(/<textarea[^>]*rows="2"[^>]*class="hatch-grows"/);
  });

  it('disables the textarea when disabled is set', () => {
    const html = renderToStaticMarkup(<RunnerProjectsField label="Projects" value="" onChange={() => {}} disabled />);

    expect(html).toMatch(/<textarea[^>]*disabled=""/);
  });

  it('disables the textarea when busy is set', () => {
    const html = renderToStaticMarkup(<RunnerProjectsField label="Projects" value="" onChange={() => {}} busy />);

    expect(html).toMatch(/<textarea[^>]*disabled=""/);
  });
});
