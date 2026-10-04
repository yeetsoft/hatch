import { describe, expect, it } from 'vitest';
import { LOGO_ACCEPTED_TYPES, LOGO_BOX, logoCrop, projectLogoUrl } from './projectLogo';

describe('LOGO_ACCEPTED_TYPES', () => {
  it('is the four formats PersonPhoto.TryDetectContentType recognises', () => {
    expect(LOGO_ACCEPTED_TYPES).toEqual(['image/png', 'image/jpeg', 'image/gif', 'image/webp']);
  });
});

describe('LOGO_BOX', () => {
  it('is the fixed square every logo is downscaled into', () => {
    expect(LOGO_BOX).toBe(256);
  });
});

describe('logoCrop', () => {
  it('centres a square on the shorter side of a landscape image', () => {
    expect(logoCrop(400, 200)).toEqual({ sx: 100, sy: 0, size: 200 });
  });

  it('centres a square on the shorter side of a portrait image', () => {
    expect(logoCrop(200, 400)).toEqual({ sx: 0, sy: 100, size: 200 });
  });

  it('takes the whole image when it is already square', () => {
    expect(logoCrop(300, 300)).toEqual({ sx: 0, sy: 0, size: 300 });
  });
});

describe('projectLogoUrl', () => {
  it('is null when the project has no logo', () => {
    expect(projectLogoUrl({ id: 1, logoUpdatedAt: null })).toBeNull();
  });

  it('points at the logo route, versioned by when it was last updated', () => {
    const url = projectLogoUrl({ id: 1, logoUpdatedAt: '2026-01-01T00:00:00Z' });
    expect(url).toBe(`/api/hatch/projects/1/logo?v=${Date.parse('2026-01-01T00:00:00Z')}`);
  });

  it('changes when the logo is replaced, so the browser never serves the old image from cache', () => {
    const before = projectLogoUrl({ id: 1, logoUpdatedAt: '2026-01-01T00:00:00Z' });
    const after = projectLogoUrl({ id: 1, logoUpdatedAt: '2026-01-02T00:00:00Z' });
    expect(before).not.toBe(after);
  });
});
