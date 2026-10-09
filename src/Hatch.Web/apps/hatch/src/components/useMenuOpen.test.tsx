import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { ThemeProvider, TopBar, useMenuOpen } from '@hatch/ui';

/* packages/ui has no test runner (see menuState.test.ts's own comment), so
   this - a hook that ships from the package - is tested here instead. No
   jsdom: renderToStaticMarkup is enough, because what's reachable under a
   static render is that the context is wired and defaults correctly, not
   that it flips (useSyncExternalStore's server snapshot always answers
   "closed"). The open path is the operator's check in the design gallery. */
function OpenProbe() {
  return <>{String(useMenuOpen())}</>;
}

describe('useMenuOpen', () => {
  it('answers false outside any menu', () => {
    const html = renderToStaticMarkup(<OpenProbe />);
    expect(html).toBe('false');
  });

  it('reaches a panel child through TopBar, closed by default', () => {
    const html = renderToStaticMarkup(
      <ThemeProvider>
        <TopBar appName="Test" menu={<OpenProbe />} />
      </ThemeProvider>,
    );
    expect(html).toContain('false');
  });
});
