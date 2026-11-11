import { describe, expect, it, vi } from 'vitest';
import { saveRunner } from './saveRunner';

describe('saveRunner', () => {
  it('patches once with the exact request, reloads, and returns null', async () => {
    const calls: string[] = [];
    const patch = vi.fn(async () => void calls.push('patch'));
    const reload = vi.fn(async () => void calls.push('reload'));

    await expect(saveRunner('Quill', { tags: ['gpu'], under: '' }, { patch, reload })).resolves.toBeNull();

    expect(patch).toHaveBeenCalledTimes(1);
    expect(patch).toHaveBeenCalledWith('Quill', { tags: ['gpu'], under: '' });
    expect(reload).toHaveBeenCalledTimes(1);
    expect(calls).toEqual(['patch', 'reload']);
  });

  it('returns the refusal and still reloads when the patch throws', async () => {
    const calls: string[] = [];
    const patch = vi.fn(async () => {
      calls.push('patch');
      throw new Error('--max-tokens-per-hour takes a count');
    });
    const reload = vi.fn(async () => void calls.push('reload'));

    await expect(saveRunner('Quill', { maxTokensPerHour: 'x' }, { patch, reload })).resolves.toBe(
      '--max-tokens-per-hour takes a count',
    );

    expect(reload).toHaveBeenCalledTimes(1);
    expect(calls).toEqual(['patch', 'reload']);
  });
});
