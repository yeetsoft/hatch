import { describe, expect, it, vi } from 'vitest';
import { setReleaseNote } from '../api/client';
import { releaseNoteShown, writeReleaseNote } from './releaseNote';

vi.mock('../api/client', () => ({ setReleaseNote: vi.fn() }));

describe('releaseNoteShown', () => {
  it('is false for a child with no note', () => {
    expect(releaseNoteShown({ parentKey: 'HA-1', releaseNote: null })).toBe(false);
  });

  it('is false for a child whose note is empty', () => {
    expect(releaseNoteShown({ parentKey: 'HA-1', releaseNote: '' })).toBe(false);
  });

  it('is false for a child whose note is only whitespace', () => {
    expect(releaseNoteShown({ parentKey: 'HA-1', releaseNote: '   ' })).toBe(false);
  });

  it('is true for a child with a note', () => {
    expect(releaseNoteShown({ parentKey: 'HA-1', releaseNote: 'shipped' })).toBe(true);
  });

  it('is true for a top-level issue with no note', () => {
    expect(releaseNoteShown({ parentKey: null, releaseNote: null })).toBe(true);
  });

  it('is true for a top-level issue with a note', () => {
    expect(releaseNoteShown({ parentKey: null, releaseNote: 'shipped' })).toBe(true);
  });
});

describe('writeReleaseNote', () => {
  it('calls setReleaseNote once and answers null on success', async () => {
    vi.mocked(setReleaseNote).mockResolvedValueOnce({} as never);

    await expect(writeReleaseNote('HA-5', 'text')).resolves.toBeNull();

    expect(setReleaseNote).toHaveBeenCalledTimes(1);
    expect(setReleaseNote).toHaveBeenCalledWith('HA-5', 'text');
  });

  it('answers the server refusal sentence instead of throwing', async () => {
    vi.mocked(setReleaseNote).mockRejectedValueOnce(new Error('a release note is at most 2000 characters'));

    await expect(writeReleaseNote('HA-5', 'text')).resolves.toBe('a release note is at most 2000 characters');
  });
});
