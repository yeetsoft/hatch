/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { HATCH_ICON_GRADIENT, HATCH_MARK_GRADIENT, HatchMark } from '@hatch/ui';

const css = readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

function lightToken(name: string): string {
  const root = /:root\s*\{([^}]*)\}/.exec(css);
  if (!root) throw new Error('no :root block');
  const match = new RegExp(`${name}:\\s*([^;]+);`).exec(root[1]);
  if (!match) throw new Error(`${name} not found on :root`);
  return match[1].trim();
}

describe('HatchMark', () => {
  it('draws in currentColor, with no gradient, by default', () => {
    const html = renderToStaticMarkup(<HatchMark />);

    expect(html).toContain('fill="currentColor"');
    expect(html).not.toContain('<defs>');
    expect(html).not.toContain('linearGradient');
  });

  it('paints from --primary to --chrome-end when gradient is set', () => {
    const html = renderToStaticMarkup(<HatchMark gradient />);

    expect(html).toMatch(/fill="url\(#[^)]+\)"/);
    expect(html).toContain('<linearGradient');
    expect(html).toContain('stop-color:var(--primary)');
    expect(html).toContain('stop-color:var(--chrome-end)');
  });

  it('matches the generated assets gradients, light values, since both are kept in step with tokens.css', () => {
    expect(HATCH_MARK_GRADIENT).toEqual([lightToken('--primary'), lightToken('--chrome-end')]);
    expect(HATCH_ICON_GRADIENT).toEqual([lightToken('--chrome'), lightToken('--chrome-end')]);
  });
});
