import { describe, expect, it } from 'vitest';
import { toMonacoHex, withAlpha } from './cssColor';

describe('toMonacoHex', () => {
  it('upper-cases a long hex and expands a short one', () => {
    expect(toMonacoHex('#0066cc')).toBe('#0066CC');
    expect(toMonacoHex('#fff')).toBe('#FFFFFF');
  });

  it('reads rgb() and rgba()', () => {
    expect(toMonacoHex('rgb(26, 26, 26)')).toBe('#1A1A1A');
    expect(toMonacoHex('rgba(0, 102, 204, 0.08)')).toBe('#0066CC14');
    expect(toMonacoHex('rgba(102, 179, 255, 0.12)')).toBe('#66B3FF1F');
  });

  it('drops the alpha when it is 1', () => {
    expect(toMonacoHex('rgba(0, 102, 204, 1)')).toBe('#0066CC');
  });

  it('tolerates the whitespace getPropertyValue leaves on a token', () => {
    expect(toMonacoHex('  #0066cc ')).toBe('#0066CC');
  });

  it('answers null for what it cannot read', () => {
    expect(toMonacoHex('var(--x)')).toBeNull();
    expect(toMonacoHex('')).toBeNull();
    expect(toMonacoHex('tomato')).toBeNull();
  });
});

describe('withAlpha', () => {
  it('appends the alpha as two hex digits', () => {
    expect(withAlpha('#0066cc', 0.25)).toBe('#0066CC40');
  });

  it('replaces an alpha the colour already had', () => {
    expect(withAlpha('#0066CC14', 1)).toBe('#0066CCFF');
  });
});
