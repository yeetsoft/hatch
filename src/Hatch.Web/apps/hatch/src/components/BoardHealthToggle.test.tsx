import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { BoardHealthToggle } from './BoardHealthToggle';

describe('BoardHealthToggle', () => {
  it('pins aria-pressed off, unpressed', () => {
    const html = renderToStaticMarkup(<BoardHealthToggle on={false} onToggle={() => {}} />);

    expect(html).toContain('aria-pressed="false"');
    expect(html).not.toContain('active');
  });

  it('pins aria-pressed on, pressed', () => {
    const html = renderToStaticMarkup(<BoardHealthToggle on={true} onToggle={() => {}} />);

    expect(html).toContain('aria-pressed="true"');
    expect(html).toContain('hatch-health-toggle active');
  });

  it('keeps one name whichever way it is pressed', () => {
    const name = (on: boolean) =>
      /aria-label="([^"]*)"/.exec(renderToStaticMarkup(<BoardHealthToggle on={on} onToggle={() => {}} />))?.[1];

    expect(name(true)).toBe(name(false));
  });
});
