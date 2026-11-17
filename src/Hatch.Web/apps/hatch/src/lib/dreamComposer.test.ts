import { describe, expect, it, vi } from 'vitest';
import { fadesOnPost, sourceOpen, submitDream } from './dreamComposer';

describe('sourceOpen', () => {
  it('is folded until pressed', () => {
    expect(sourceOpen(false, '')).toBe(false);
  });

  it('opens on a press', () => {
    expect(sourceOpen(true, '')).toBe(true);
  });

  it('stays open once something is typed in it', () => {
    expect(sourceOpen(false, 'a talk')).toBe(true);
  });
});

describe('submitDream', () => {
  it('posts the body, and the source trimmed', async () => {
    const onPost = vi.fn().mockResolvedValue(undefined);

    const outcome = await submitDream('a line', '  a talk ', false, onPost);

    expect(onPost).toHaveBeenCalledWith('a line', 'a talk');
    expect(outcome).toEqual({ kind: 'posted', body: 'a line' });
  });

  it('sends no source when it is blank', async () => {
    const onPost = vi.fn().mockResolvedValue(undefined);

    await submitDream('a line', '   ', false, onPost);

    expect(onPost).toHaveBeenCalledWith('a line', null);
  });

  it('skips an empty box and a post already in flight', async () => {
    const onPost = vi.fn().mockResolvedValue(undefined);

    expect(await submitDream('  \n', '', false, onPost)).toEqual({ kind: 'skipped' });
    expect(await submitDream('a line', '', true, onPost)).toEqual({ kind: 'skipped' });
    expect(onPost).not.toHaveBeenCalled();
  });

  it('carries a refusal as its reason', async () => {
    const onPost = vi.fn().mockRejectedValue(new Error('Too long'));

    const outcome = await submitDream('a line', 'a talk', false, onPost);

    expect(outcome).toEqual({ kind: 'refused', reason: 'Too long' });
  });
});

describe('fadesOnPost', () => {
  it('falls unless motion is reduced', () => {
    expect(fadesOnPost(false)).toBe(true);
    expect(fadesOnPost(true)).toBe(false);
  });
});
