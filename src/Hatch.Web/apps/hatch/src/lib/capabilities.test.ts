import { describe, expect, it } from 'vitest';
import { capabilityStateLabel, capabilityStateTone } from './capabilities';

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

describe('capabilityStateTone', () => {
  it('tones the four states', () => {
    expect(capabilityStateTone('holds')).toBe('success');
    expect(capabilityStateTone('does_not_hold')).toBe('danger');
    expect(capabilityStateTone('cannot_tell')).toBe('warn');
    expect(capabilityStateTone('not_checked')).toBe('muted');
  });

  it('gives an unknown state the resting tone', () => {
    expect(capabilityStateTone('something_else')).toBe('muted');
  });
});
