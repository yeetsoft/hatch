import { describe, expect, it } from 'vitest';
import { hasNewer, offsetFor, parsePage } from './history';

function params(page?: string): URLSearchParams {
  const params = new URLSearchParams();
  if (page !== undefined) params.set('page', page);
  return params;
}

describe('parsePage', () => {
  it('reads a missing page as 1', () => {
    expect(parsePage(params())).toBe(1);
  });

  it('reads an unparseable page as 1', () => {
    expect(parsePage(params('nope'))).toBe(1);
  });

  it('reads a zero page as 1', () => {
    expect(parsePage(params('0'))).toBe(1);
  });

  it('reads a negative page as 1', () => {
    expect(parsePage(params('-3'))).toBe(1);
  });

  it('reads a fractional page as 1', () => {
    expect(parsePage(params('2.5'))).toBe(1);
  });

  it('reads a positive integer as itself', () => {
    expect(parsePage(params('3'))).toBe(3);
  });
});

describe('offsetFor', () => {
  it('reads page 3 as an offset of 200, a hundred to a page', () => {
    expect(offsetFor(3)).toBe(200);
  });

  it('reads page 1 as no offset', () => {
    expect(offsetFor(1)).toBe(0);
  });
});

describe('hasNewer', () => {
  it('is false on page 1, the only page with nothing to go newer to', () => {
    expect(hasNewer(1)).toBe(false);
  });

  it('is true on any later page', () => {
    expect(hasNewer(2)).toBe(true);
  });
});
