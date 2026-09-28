import { describe, expect, it } from 'vitest';
import { createLedger, mayApply, mayRefresh } from './refresh';

describe('mayRefresh', () => {
  it('is false while the page is hidden', () => {
    expect(mayRefresh({ visible: false, paused: false })).toBe(false);
  });

  it('is false while paused', () => {
    expect(mayRefresh({ visible: true, paused: true })).toBe(false);
  });

  it('is false when hidden and paused', () => {
    expect(mayRefresh({ visible: false, paused: true })).toBe(false);
  });

  it('is true when visible and not paused', () => {
    expect(mayRefresh({ visible: true, paused: false })).toBe(true);
  });
});

describe('mayApply', () => {
  it('applies an explicit, current answer while paused', () => {
    expect(mayApply({ background: false, current: true, paused: true })).toBe(true);
  });

  it('applies a background, current answer when not paused', () => {
    expect(mayApply({ background: true, current: true, paused: false })).toBe(true);
  });

  it('drops a background, current answer that resolves while paused', () => {
    expect(mayApply({ background: true, current: true, paused: true })).toBe(false);
  });

  it('drops an answer that is not current, whatever else is true', () => {
    for (const background of [true, false]) {
      for (const paused of [true, false]) {
        expect(mayApply({ background, current: false, paused })).toBe(false);
      }
    }
  });
});

describe('createLedger', () => {
  it('holds the only load begun as current', () => {
    const ledger = createLedger();
    expect(ledger.isCurrent(ledger.begin())).toBe(true);
  });

  it('drops the older of two loads, however they resolve', () => {
    const ledger = createLedger();
    const first = ledger.begin();
    const second = ledger.begin();

    // The second resolves first, then the first: only the second is kept.
    expect(ledger.isCurrent(second)).toBe(true);
    expect(ledger.isCurrent(first)).toBe(false);
  });

  it('drops a load that was in flight when local state was written', () => {
    const ledger = createLedger();
    const ticket = ledger.begin();

    ledger.invalidate();

    expect(ledger.isCurrent(ticket)).toBe(false);
  });

  it('keeps a load begun after the write', () => {
    const ledger = createLedger();
    ledger.begin();
    ledger.invalidate();

    expect(ledger.isCurrent(ledger.begin())).toBe(true);
  });

  it('drops every load in flight on a second write', () => {
    const ledger = createLedger();
    const a = ledger.begin();
    ledger.invalidate();
    const b = ledger.begin();
    ledger.invalidate();

    expect(ledger.isCurrent(a)).toBe(false);
    expect(ledger.isCurrent(b)).toBe(false);
  });

  it('keeps separate ledgers apart', () => {
    const one = createLedger();
    const other = createLedger();
    const ticket = one.begin();

    other.begin();
    other.invalidate();

    expect(one.isCurrent(ticket)).toBe(true);
  });
});
