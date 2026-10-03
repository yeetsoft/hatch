/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const html = readFileSync(new URL('../../index.html', import.meta.url), 'utf8');

describe('index.html', () => {
  it('links the manifest', () => {
    expect(html).toMatch(/<link rel="manifest" href="\/manifest\.webmanifest"[^>]*>/);
  });

  it('serves the manifest with credentials', () => {
    const match = /<link rel="manifest"[^>]*>/.exec(html);
    expect(match?.[0]).toContain('crossorigin="use-credentials"');
  });
});
