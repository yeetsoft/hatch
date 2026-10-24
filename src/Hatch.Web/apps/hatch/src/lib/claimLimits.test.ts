import { describe, expect, it } from 'vitest';
import { claimLimitsSentence } from './claimLimits';

describe('claimLimitsSentence', () => {
  it('says every limit follows the install when the project sets none', () => {
    expect(claimLimitsSentence({ stallLapseMinutes: null, stallGraceMaxMinutes: null, claimLapseLimit: null })).toBe(
      'Claims: quiet window follows this install, grace cap follows this install, lapse limit follows this install.',
    );
  });

  it('reads a project that sets its own', () => {
    expect(claimLimitsSentence({ stallLapseMinutes: 15, stallGraceMaxMinutes: 90, claimLapseLimit: 3 })).toBe(
      'Claims: quiet window 15 min, grace cap 90 min, lapse limit 3.',
    );
  });

  it('reads zero as off, and a project that predates the fields as following the install', () => {
    expect(claimLimitsSentence({ stallLapseMinutes: 0, stallGraceMaxMinutes: 0, claimLapseLimit: 0 })).toBe(
      'Claims: quiet window off, grace cap off, lapse limit off.',
    );
    expect(claimLimitsSentence({})).toContain('follows this install');
  });
});
