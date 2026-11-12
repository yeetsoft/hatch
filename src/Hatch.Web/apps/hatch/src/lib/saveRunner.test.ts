import { describe, expect, it, vi } from 'vitest';
import { saveRunner, saveRunnerSettings } from './saveRunner';

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


describe('saveRunnerSettings', () => {
  const setup = (over: { patch?: () => Promise<void>; putRefused?: () => Promise<void> } = {}) => {
    const calls: string[] = [];
    const patch = vi.fn(
      over.patch ??
        (async () => {
          calls.push('patch');
        }),
    );
    const putRefused = vi.fn(
      over.putRefused ??
        (async () => {
          calls.push('put');
        }),
    );
    const reload = vi.fn(async () => void calls.push('reload'));
    return { calls, patch, putRefused, reload };
  };

  const WRONG_AGENT = '"gpt-6-luna" is a codex model - this runner\'s agent is claude';

  it('sends only the patch when the list is unchanged, then reloads once', async () => {
    const { calls, patch, putRefused, reload } = setup();
    const request = { patch: { tags: ['gpu'], under: '' }, refusedModels: ['fable'] };

    await expect(
      saveRunnerSettings('Quill', request, ['fable'], 'claude', { patch, putRefused, reload }),
    ).resolves.toBeNull();

    expect(patch).toHaveBeenCalledWith('Quill', { tags: ['gpu'], under: '' });
    expect(putRefused).not.toHaveBeenCalled();
    expect(calls).toEqual(['patch', 'reload']);
  });

  it('sends the patch, then the whole list, then one reload, when the list changed', async () => {
    const { calls, patch, putRefused, reload } = setup();
    const request = { patch: { tags: [], under: '' }, refusedModels: ['fable', 'haiku'] };

    await expect(
      saveRunnerSettings('Quill', request, ['fable'], 'claude', { patch, putRefused, reload }),
    ).resolves.toBeNull();

    expect(putRefused).toHaveBeenCalledWith('Quill', ['fable', 'haiku']);
    expect(calls).toEqual(['patch', 'put', 'reload']);
  });

  it('sends the list when it is cleared', async () => {
    const { patch, putRefused, reload } = setup();
    const request = { patch: { tags: [], under: '' }, refusedModels: [] };

    await saveRunnerSettings('Quill', request, ['fable'], null, { patch, putRefused, reload });

    expect(putRefused).toHaveBeenCalledWith('Quill', []);
  });

  it('returns the patch refusal, sends no put, and still reloads', async () => {
    const { calls, patch, putRefused, reload } = setup({
      patch: async () => {
        throw new Error('an epic called NOPE does not exist');
      },
    });
    const request = { patch: { tags: [], under: 'NOPE-1' }, refusedModels: ['fable'] };

    await expect(saveRunnerSettings('Quill', request, [], 'claude', { patch, putRefused, reload })).resolves.toBe(
      'an epic called NOPE does not exist',
    );

    expect(putRefused).not.toHaveBeenCalled();
    expect(calls).toEqual(['reload']);
  });

  it('returns the put refusal and still reloads', async () => {
    const { calls, patch, putRefused, reload } = setup({
      putRefused: async () => {
        throw new Error('the board refused it');
      },
    });
    const request = { patch: { tags: [], under: '' }, refusedModels: ['fable'] };

    await expect(saveRunnerSettings('Quill', request, [], null, { patch, putRefused, reload })).resolves.toBe(
      'the board refused it',
    );

    expect(patch).toHaveBeenCalledTimes(1);
    expect(calls).toEqual(['patch', 'reload']);
  });

  it('refuses a bad entry in a changed list with the API sentence, sending and reloading nothing', async () => {
    const { calls, patch, putRefused, reload } = setup();
    const request = { patch: { tags: [], under: '' }, refusedModels: ['fable', 'gpt-6-luna'] };

    await expect(saveRunnerSettings('Quill', request, [], 'claude', { patch, putRefused, reload })).resolves.toBe(
      WRONG_AGENT,
    );

    expect(patch).not.toHaveBeenCalled();
    expect(putRefused).not.toHaveBeenCalled();
    expect(calls).toEqual([]);
  });

  it('does not judge a list it was not asked to change', async () => {
    const { patch, putRefused, reload } = setup();
    const request = { patch: { tags: ['gpu'], under: '' }, refusedModels: ['gpt-6-luna'] };

    await expect(
      saveRunnerSettings('Quill', request, ['gpt-6-luna'], 'claude', { patch, putRefused, reload }),
    ).resolves.toBeNull();

    expect(patch).toHaveBeenCalledTimes(1);
    expect(putRefused).not.toHaveBeenCalled();
  });
});
