import { describe, expect, it } from 'vitest';
import { POLL_MS } from './useUtilization';

/* No DOM test environment exists (see the header of useScope.test.tsx), so
   the hook's effects - the poll itself, visibilitychange - cannot run here.
   The driver covers them, per the ticket. This only checks the constant. */
describe('useUtilization', () => {
  it('polls every thirty seconds', () => {
    expect(POLL_MS).toBe(30000);
  });
});
