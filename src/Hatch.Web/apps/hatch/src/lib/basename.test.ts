import { describe, expect, it } from 'vitest';
import { APP_BASENAME, fullHref, hrefWithin, isBoardPath, routerBasename } from './basename';

describe('routerBasename', () => {
  it('is always the root, whatever the pathname', () => {
    expect(routerBasename('/')).toBe('/');
    expect(routerBasename('/issues/AER-12')).toBe('/');
    expect(routerBasename('/apps/hatch/')).toBe('/');
    expect(routerBasename('/apps/hatchery/')).toBe('/');
  });
});

describe('hrefWithin', () => {
  it('changes nothing - the only basename there is now is the root', () => {
    expect(hrefWithin(APP_BASENAME, '/issues/AER-12')).toBe('/issues/AER-12');
    expect(hrefWithin('/', '/issues/AER-12')).toBe('/issues/AER-12');
  });
});

describe('fullHref', () => {
  it('is origin plus path, regardless of what the pathname carries', () => {
    expect(fullHref('https://hatch.example', '/issues/HA-446', '/issues/HA-446')).toBe(
      'https://hatch.example/issues/HA-446'
    );
    expect(fullHref('https://home.example', '/apps/hatch/issues/HA-446', '/issues/HA-446')).toBe(
      'https://home.example/issues/HA-446'
    );
  });
});

describe('isBoardPath', () => {
  it('is true for the root and for every route App.tsx mounts', () => {
    expect(isBoardPath('/')).toBe(true);
    expect(isBoardPath('/plan')).toBe(true);
    expect(isBoardPath('/issues/AER-12')).toBe(true);
    expect(isBoardPath('/issues/AER-12?x=1')).toBe(true);
    expect(isBoardPath('/issues/AER-12#x')).toBe(true);
    expect(isBoardPath('/docs/getting-started')).toBe(true);
  });

  it('is false for a path with no segment this app owns', () => {
    expect(isBoardPath('/apps/hatch')).toBe(false);
    expect(isBoardPath('/apps/hatchery/x')).toBe(false);
    expect(isBoardPath('https://cli.github.com/')).toBe(false);
    expect(isBoardPath('//example.com/plan')).toBe(false);
    expect(isBoardPath('')).toBe(false);
  });
});
