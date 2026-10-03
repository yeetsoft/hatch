/// <reference types="node" />
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import sharp from 'sharp';

const manifest = JSON.parse(
  readFileSync(new URL('../../public/manifest.webmanifest', import.meta.url), 'utf8'),
);

// Mirrors tokens.test.ts's declarationsOf rather than importing it, to keep
// per-file independence.
const css = readFileSync(new URL('../../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
);

function declarationsOf(selectorPattern: RegExp): Record<string, string> {
  const match = selectorPattern.exec(css);
  if (!match) throw new Error(`No match for ${selectorPattern}`);
  const body = match[1];
  const props: Record<string, string> = {};
  for (const statement of body.split(';')) {
    const trimmed = statement.trim();
    if (!trimmed) continue;
    const colon = trimmed.indexOf(':');
    if (colon === -1) continue;
    const prop = trimmed.slice(0, colon).trim();
    const value = trimmed.slice(colon + 1).trim().replace(/\s+/g, ' ');
    props[prop] = value;
  }
  return props;
}

const light = declarationsOf(/:root\s*\{([^}]*)\}/);

describe('manifest.webmanifest', () => {
  it('declares the PWA identity fields exactly', () => {
    expect(manifest.scope).toBe('/');
    expect(manifest.start_url).toBe('/apps/hatch/');
    expect(manifest.id).toBe('/apps/hatch/');
    expect(manifest.display).toBe('standalone');
    expect(manifest.launch_handler.client_mode).toBe('navigate-existing');
  });

  it('matches tokens.css for theme_color and background_color', () => {
    expect(manifest.theme_color).toBe(light['--chrome']);
    expect(manifest.background_color).toBe(light['--bg']);
  });

  it('names icons that exist on disk at their declared size', async () => {
    expect(manifest.icons.length).toBeGreaterThan(0);
    for (const icon of manifest.icons as Array<{ src: string; sizes: string }>) {
      const relative = icon.src.replace(/^\/apps\/hatch\//, '');
      const path = fileURLToPath(new URL(`../../public/${relative}`, import.meta.url));
      expect(existsSync(path), `${icon.src} does not exist at ${path}`).toBe(true);

      const [width, height] = icon.sizes.split('x').map(Number);
      const metadata = await sharp(path).metadata();
      expect(metadata.width, `${icon.src} width`).toBe(width);
      expect(metadata.height, `${icon.src} height`).toBe(height);
    }
  });
});
