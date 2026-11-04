import { describe, expect, it, vi } from 'vitest';
import type { RunnerOmission } from '../types';
import {
  isNotARunner,
  isOwnOmission,
  omissionDraft,
  omissionReason,
  omissionWho,
  omissionsFact,
  reasonCounter,
  submitOmission,
} from './runnerOmissions';

function omission(over: Partial<RunnerOmission> = {}): RunnerOmission {
  return { runner: 'runner-a', reason: null, by: 'person-a', at: '2026-01-01T00:00:00Z', fromKey: null, ...over };
}

describe('isOwnOmission', () => {
  it('is true for the issue\'s own row', () => {
    expect(isOwnOmission(omission())).toBe(true);
  });

  it('is false for one inherited from an ancestor', () => {
    expect(isOwnOmission(omission({ fromKey: 'AER-1' }))).toBe(false);
  });
});

describe('omissionWho', () => {
  it('says the runner recused itself when it is its own author', () => {
    expect(omissionWho(omission({ by: 'runner-a' }))).toBe('recused itself');
  });

  it('names the person who omitted it', () => {
    expect(omissionWho(omission({ by: 'person-a' }))).toBe('omitted by person-a');
  });
});

describe('omissionReason', () => {
  it('says so when there is no reason', () => {
    expect(omissionReason(omission())).toBe('no reason given');
  });

  it('gives the reason as written', () => {
    expect(omissionReason(omission({ reason: 'no toolchain' }))).toBe('no toolchain');
  });
});

describe('isNotARunner', () => {
  it('is false for a name in the list', () => {
    expect(isNotARunner('runner-a', ['runner-a', 'runner-b'])).toBe(false);
  });

  it('is true for a name absent from the list', () => {
    expect(isNotARunner('runner-c', ['runner-a', 'runner-b'])).toBe(true);
  });

  it('marks nothing while the list is not known', () => {
    expect(isNotARunner('runner-c', null)).toBe(false);
  });
});

describe('reasonCounter', () => {
  it('counts an empty draft', () => {
    expect(reasonCounter('')).toBe('0/200');
  });

  it('counts a short draft', () => {
    expect(reasonCounter('abc')).toBe('3/200');
  });
});

describe('omissionDraft', () => {
  it('trims both fields', () => {
    expect(omissionDraft('  runner-a ', '  no toolchain  ')).toEqual({ runner: 'runner-a', reason: 'no toolchain' });
  });

  it('is null for a blank runner', () => {
    expect(omissionDraft('   ', 'no toolchain')).toBeNull();
  });
});

describe('omissionsFact', () => {
  it('gives an own row with its reason', () => {
    expect(omissionsFact([omission({ reason: 'no toolchain' })])).toBe('runner-a (no toolchain)');
  });

  it('gives an inherited row without a reason, naming where it came from', () => {
    expect(omissionsFact([omission({ fromKey: 'AER-1' })])).toBe('runner-a · from AER-1');
  });

  it('joins rows with a semicolon', () => {
    expect(
      omissionsFact([omission({ reason: 'no toolchain' }), omission({ runner: 'runner-b', fromKey: 'AER-1' })]),
    ).toBe('runner-a (no toolchain); runner-b · from AER-1');
  });

  it('is empty for no rows', () => {
    expect(omissionsFact([])).toBe('');
  });
});

describe('submitOmission', () => {
  it('posts the typed runner and reason', async () => {
    const omit = vi.fn().mockResolvedValue(undefined);
    const result = await submitOmission(omit, 'AER-1', 'runner-a', 'no toolchain');
    expect(omit).toHaveBeenCalledTimes(1);
    expect(omit).toHaveBeenCalledWith('AER-1', 'runner-a', { reason: 'no toolchain' });
    expect(result).toEqual({ ok: true });
  });

  it('resolves the refusal sentence when the call throws', async () => {
    const omit = vi.fn().mockRejectedValue(new Error('an omission reason is at most 200 characters'));
    const result = await submitOmission(omit, 'AER-1', 'runner-a', 'x');
    expect(result).toEqual({ ok: false, refusal: 'an omission reason is at most 200 characters' });
  });
});
