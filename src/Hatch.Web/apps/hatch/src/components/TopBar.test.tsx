import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ThemeProvider, TopBar } from '@hatch/ui';

/* packages/ui has no test runner (see menuState.test.ts's own comment), so
   this - the first component-render test anywhere in the app - lives here.
   No jsdom, no @testing-library/react: react-dom/server's
   renderToStaticMarkup is already resolvable and runs under vitest's default
   node environment. MenuRoot's useSyncExternalStore server snapshot answers
   "closed", but the panel markup is always present (visibility: hidden via
   CSS, not unmounted), so a static render is enough to check structure. */
describe('TopBar', () => {
  it('puts the gear trigger and the Theme row ahead of the app menu content', () => {
    const html = renderToStaticMarkup(
      <ThemeProvider>
        <TopBar appName="Test" menu={<a href="/x">X marks the spot</a>} />
      </ThemeProvider>,
    );

    expect(html).toMatch(/<button[^>]*aria-label="Settings and theme"/);

    const themeIndex = html.indexOf('<fieldset');
    const menuIndex = html.indexOf('X marks the spot');
    expect(themeIndex).toBeGreaterThan(-1);
    expect(menuIndex).toBeGreaterThan(themeIndex);
  });

  it('carries the full-width modifier only when asked for it', () => {
    const full = renderToStaticMarkup(
      <ThemeProvider>
        <TopBar appName="Test" width="full" />
      </ThemeProvider>,
    );
    const measure = renderToStaticMarkup(
      <ThemeProvider>
        <TopBar appName="Test" />
      </ThemeProvider>,
    );

    expect(full).toContain('hatch-topbar__inner--full');
    expect(measure).not.toContain('hatch-topbar__inner--full');
  });
});
