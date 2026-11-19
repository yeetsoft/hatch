import { describe, expect, it } from 'vitest';
import { capabilityStateLabel } from './capabilities';

describe('capabilityStateLabel', () => {
  it('names the four words', () => {
    expect(capabilityStateLabel('holds')).toBe('Holds');
    expect(capabilityStateLabel('does_not_hold')).toBe('Does not hold');
    expect(capabilityStateLabel('cannot_tell')).toBe('Cannot tell');
    expect(capabilityStateLabel('not_checked')).toBe('Not checked');
  });

  it('gives an unknown state back unchanged', () => {
    expect(capabilityStateLabel('something_else')).toBe('something_else');
  });
});
