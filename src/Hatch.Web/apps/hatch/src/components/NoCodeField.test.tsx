import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { NoCodeField } from './NoCodeField';

const render = (over: Partial<Parameters<typeof NoCodeField>[0]> = {}) =>
  renderToStaticMarkup(<NoCodeField noCode={null} busy={false} error={null} onSave={async () => true} {...over} />);

/** The opening tag of the button with this label. */
const button = (html: string, label: string) => html.match(new RegExp(`<button[^>]*>${label}<`))?.[0] ?? '';

describe('NoCodeField', () => {
  it('offers Save but no Clear while nothing is declared', () => {
    const html = render();
    expect(html).toContain('<input');
    expect(html).toContain('>Save<');
    expect(html).not.toContain('>Clear<');
  });

  it('shows the saved reason with Clear, and Save waits for an edit', () => {
    const html = render({ noCode: 'x' });
    expect(html).toContain('value="x"');
    expect(html).toContain('>Clear<');
    expect(button(html, 'Save')).toContain('disabled=""');
    expect(button(html, 'Clear')).not.toContain('disabled');
  });

  it("shows the API's refusal verbatim under the controls", () => {
    const sentence = 'HA-5 has a pull request recorded - it did not finish with no code';
    expect(render({ error: sentence })).toContain(`<p class="text-danger">${sentence}</p>`);
    expect(render()).not.toContain('text-danger');
  });

  it('disables the input and both buttons while busy', () => {
    const html = render({ noCode: 'x', busy: true });
    expect(html.match(/<input[^>]*>/)?.[0]).toContain('disabled=""');
    expect(button(html, 'Save')).toContain('disabled=""');
    expect(button(html, 'Clear')).toContain('disabled=""');
  });

  it('caps the input at the length the API accepts', () => {
    expect(render()).toContain('maxLength="200"');
  });
});
