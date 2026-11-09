import { describe, expect, it, vi } from 'vitest';
import { setIssueNoCode } from '../api/client';
import { noCodeDraft, noCodeRequest, writeNoCode } from './noCode';

vi.mock('../api/client', () => ({ setIssueNoCode: vi.fn() }));

describe('noCodeDraft', () => {
  it('is empty for no reason', () => {
    expect(noCodeDraft(null)).toBe('');
  });

  it('is the reason itself', () => {
    expect(noCodeDraft('x')).toBe('x');
  });
});

describe('noCodeRequest', () => {
  it('trims the draft', () => {
    expect(noCodeRequest('  because  ')).toEqual({ reason: 'because' });
  });

  it('trims whitespace alone down to empty', () => {
    expect(noCodeRequest('   ')).toEqual({ reason: '' });
  });
});

describe('writeNoCode', () => {
  it('calls setIssueNoCode once and answers null on success', async () => {
    vi.mocked(setIssueNoCode).mockResolvedValueOnce({} as never);

    await expect(writeNoCode('HA-5', 'x')).resolves.toBeNull();

    expect(setIssueNoCode).toHaveBeenCalledTimes(1);
    expect(setIssueNoCode).toHaveBeenCalledWith('HA-5', { reason: 'x' });
  });

  it('sends an empty reason to clear it', async () => {
    vi.mocked(setIssueNoCode).mockResolvedValueOnce({} as never);

    await writeNoCode('HA-5', '');

    expect(setIssueNoCode).toHaveBeenCalledWith('HA-5', { reason: '' });
  });

  it('answers the server refusal sentence instead of throwing', async () => {
    vi.mocked(setIssueNoCode).mockRejectedValueOnce(
      new Error('HA-5 has a pull request recorded - it did not finish with no code')
    );

    await expect(writeNoCode('HA-5', 'x')).resolves.toBe(
      'HA-5 has a pull request recorded - it did not finish with no code'
    );
  });
});
