import { describe, expect, it } from 'vitest';
import { APP_BASENAME, fullHref, hrefWithin, routerBasename } from './basename';

describe('routerBasename', () => {
  it('is the prefix when served under it on the house hostname', () => {
    expect(routerBasename('/apps/hatch/')).toBe(APP_BASENAME);
    expect(routerBasename('/apps/hatch')).toBe(APP_BASENAME);
    expect(routerBasename('/apps/hatch/issues/AER-12')).toBe(APP_BASENAME);
  });

  it('is the root on the hatch hostname, where the rewrite is invisible', () => {
    expect(routerBasename('/')).toBe('/');
    expect(routerBasename('/issues/AER-12')).toBe('/');
    expect(routerBasename('/import')).toBe('/');
  });

  it('matches by segment, so a neighbouring app is not mistaken for this one', () => {
    expect(routerBasename('/apps/hatchery/')).toBe('/');
    expect(routerBasename('/apps/admin/')).toBe('/');
  });
});

describe('hrefWithin', () => {
  it('names the prefix on the house hostname, where a raw anchor would otherwise miss it', () => {
    expect(hrefWithin(APP_BASENAME, '/issues/AER-12')).toBe('/apps/hatch/issues/AER-12');
  });

  it('changes nothing on the hatch hostname', () => {
    expect(hrefWithin('/', '/issues/AER-12')).toBe('/issues/AER-12');
  });
});

describe('fullHref', () => {
  it('is origin plus path on the hatch hostname, where the pathname carries no app prefix', () => {
    expect(fullHref('https://hatch.example', '/issues/HA-446', '/issues/HA-446')).toBe(
      'https://hatch.example/issues/HA-446'
    );
  });

  it('is origin plus the app-prefixed href on the house hostname', () => {
    expect(fullHref('https://home.example', '/apps/hatch/issues/HA-446', '/issues/HA-446')).toBe(
      'https://home.example/apps/hatch/issues/HA-446'
    );
  });
});
