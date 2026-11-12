import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ThemeProvider, ThemeSwitch } from '@hatch/ui';

/* A static render, as TopBar.test.tsx does: no jsdom here, so this holds the
   structure and the names a screen reader gets. The behaviour - roll, paste,
   refuse, copy - is for the driver. */
function render() {
  return renderToStaticMarkup(
    <ThemeProvider>
      <ThemeSwitch />
    </ThemeProvider>,
  );
}

describe('ThemeSwitch', () => {
  const html = render();

  it('keeps its three radios under a Theme legend', () => {
    expect(html.match(/type="radio"/g)).toHaveLength(3);
    expect(html).toMatch(/<legend[^>]*>Theme<\/legend>/);
  });

  it('puts the palette line after the radios', () => {
    expect(html.indexOf('role="switch"')).toBeGreaterThan(html.lastIndexOf('type="radio"'));
  });

  it('has a Random switch, off with no palette stored', () => {
    expect(html).toMatch(/<input[^>]*type="checkbox"[^>]*role="switch"/);
    expect(html).not.toMatch(/role="switch"[^>]*checked/);
    expect(html).toContain('>Random<');
  });

  it('has a named dice, disabled while Random is off', () => {
    expect(html).toMatch(/<button[^>]*aria-label="Roll a new theme"[^>]*disabled/);
  });

  it('has a labelled seed field and a named copy button, disabled with no seed', () => {
    expect(html).toMatch(/<input[^>]*aria-label="Theme seed"/);
    expect(html).toMatch(/<button[^>]*aria-label="Copy seed"[^>]*disabled/);
  });

  it('has a named copy-link button after the copy-seed button, disabled with no seed', () => {
    expect(html).toMatch(/<button[^>]*aria-label="Copy link"[^>]*disabled/);
    expect(html.indexOf('aria-label="Copy link"')).toBeGreaterThan(html.indexOf('aria-label="Copy seed"'));
  });

  it('has a status region for the copy announcement, and no alert until a seed is refused', () => {
    expect(html).toMatch(/<span[^>]*role="status"/);
    expect(html).not.toContain('role="alert"');
  });
});
