import { describe, expect, it, vi } from 'vitest';
import { askToDelete, deleteQuestion } from './deletion';
import { message } from './errors';

describe('deleteQuestion', () => {
  it('names the key it is asking about', () => {
    expect(deleteQuestion('AER-1')).toBe('Delete AER-1? Its comments and its history go with it.');
  });
});

describe('askToDelete', () => {
  it('asks the one sentence', async () => {
    const ask = vi.fn(() => false);

    await askToDelete('AER-1', ask, vi.fn());

    expect(ask).toHaveBeenCalledWith(deleteQuestion('AER-1'));
  });

  it('sends nothing when the question is declined', async () => {
    const remove = vi.fn(() => Promise.resolve());

    const done = await askToDelete('AER-1', () => false, remove);

    expect(done).toEqual({ outcome: 'cancelled' });
    expect(remove).not.toHaveBeenCalled();
  });

  it('deletes once when the question is confirmed', async () => {
    const remove = vi.fn(() => Promise.resolve());

    const done = await askToDelete('AER-1', () => true, remove);

    expect(done).toEqual({ outcome: 'deleted' });
    expect(remove).toHaveBeenCalledTimes(1);
    expect(remove).toHaveBeenCalledWith('AER-1');
  });

  it('answers refused with the sentence the rejection makes', async () => {
    const failure = new Error('nope');
    const remove = vi.fn(() => Promise.reject(failure));

    const done = await askToDelete('AER-1', () => true, remove);

    expect(done).toEqual({ outcome: 'refused', error: message(failure) });
  });
});
