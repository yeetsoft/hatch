import { describe, expect, it } from 'vitest';
import { backupState, canRunNow, errorWords, folderWords, keptWords, lastSuccessWords, OFF_WORDS } from './backup';
import type { BackupStatus, BackupCopy } from '../types';

const LOCALE = 'en-US';
const ZONE = 'America/Los_Angeles';

const copy = (over: Partial<BackupCopy> = {}): BackupCopy => ({
  name: 'hatch-20260101T120000Z.sql',
  bytes: 1_048_576,
  takenAt: '2026-01-01T12:00:00Z',
  ...over,
});

const status = (over: Partial<BackupStatus> = {}): BackupStatus => ({
  configured: true,
  path: '/srv/backups',
  keepDays: 7,
  lastSuccessfulAt: null,
  lastAttemptAt: null,
  lastOutcome: 'never',
  lastError: null,
  folderReadable: true,
  folderProblem: null,
  copies: [],
  ...over,
});

describe('backupState', () => {
  it('is off with no folder set', () => {
    expect(backupState(status({ configured: false }))).toBe('off');
  });

  it('is none with a folder and no copies yet', () => {
    expect(backupState(status({ copies: [] }))).toBe('none');
  });

  it('is kept once there is at least one copy', () => {
    expect(backupState(status({ copies: [copy()] }))).toBe('kept');
  });
});

describe('the four states, in words', () => {
  it('off: one sentence, and nothing else', () => {
    expect(OFF_WORDS).toBe('No backup folder is set, so nothing is being backed up.');
  });

  it('folder with no copy: named, and never succeeded', () => {
    const s = status({ copies: [] });
    expect(folderWords(s)).toBe('Folder: /srv/backups');
    expect(lastSuccessWords(s)).toBe('There has never been a successful backup.');
  });

  it('folder with copies: date and time, count, total size, folder', () => {
    const s = status({
      lastSuccessfulAt: '2026-01-01T12:00:00Z',
      lastOutcome: 'ok',
      copies: [copy({ bytes: 1_048_576 }), copy({ bytes: 11_534_336 })],
    });

    expect(lastSuccessWords(s, LOCALE, ZONE)).toBe('Last successful backup: Jan 1, 2026, 4:00 AM');
    expect(keptWords(s)).toBe('2 copies kept, 12.0 MB in all');
    expect(folderWords(s)).toBe('Folder: /srv/backups');
  });

  it('last attempt failed: the error sentence stands beside an older success', () => {
    const s = status({
      lastSuccessfulAt: '2025-12-30T12:00:00Z',
      lastAttemptAt: '2026-01-01T12:00:00Z',
      lastOutcome: 'failed',
      lastError: 'disk full',
      copies: [copy()],
    });

    expect(lastSuccessWords(s, LOCALE, ZONE)).toContain('Dec 30, 2025');
    expect(errorWords(s, LOCALE, ZONE)).toBe('The last attempt failed: disk full (Jan 1, 2026, 4:00 AM)');
  });
});

describe('time is local', () => {
  it('reads the pinned zone\'s wall time, not the UTC stamp', () => {
    const s = status({ lastSuccessfulAt: '2026-01-01T12:00:00Z', lastOutcome: 'ok', copies: [copy()] });

    const words = lastSuccessWords(s, LOCALE, ZONE);
    expect(words).toContain('4:00 AM');
    expect(words).not.toContain('12:00 PM');
  });

  it('never puts a file name in any sentence', () => {
    const s = status({
      lastSuccessfulAt: '2026-01-01T12:00:00Z',
      lastAttemptAt: '2026-01-01T12:00:00Z',
      lastOutcome: 'failed',
      lastError: 'disk full',
      copies: [copy()],
    });

    const words = [
      OFF_WORDS,
      lastSuccessWords(s, LOCALE, ZONE),
      keptWords(s),
      folderWords(s),
      errorWords(s, LOCALE, ZONE),
    ].join(' ');

    expect(words).not.toMatch(/hatch-\d{8}T\d{6}Z/);
  });
});

describe('keptWords', () => {
  it('is singular at one copy', () => {
    expect(keptWords(status({ copies: [copy({ bytes: 512 })] }))).toBe('1 copy kept, 512 bytes in all');
  });

  it('is plural otherwise', () => {
    expect(keptWords(status({ copies: [copy(), copy()] }))).toBe('2 copies kept, 2.0 MB in all');
  });
});

describe('folderWords', () => {
  it('names the folder when it can be read', () => {
    expect(folderWords(status())).toBe('Folder: /srv/backups');
  });

  it('says the problem instead when the folder cannot be read, naming the folder itself', () => {
    const s = status({ folderReadable: false, folderProblem: '/srv/backups does not exist' });
    expect(folderWords(s)).toBe('/srv/backups does not exist');
  });

  it('a listable folder with a failed write shows copies and the error together', () => {
    const s = status({
      folderReadable: true,
      lastAttemptAt: '2026-01-01T12:00:00Z',
      lastOutcome: 'failed',
      lastError: 'disk full',
      copies: [copy(), copy()],
    });

    expect(keptWords(s)).toBe('2 copies kept, 2.0 MB in all');
    expect(errorWords(s, LOCALE, ZONE)).not.toBeNull();
  });
});

describe('errorWords', () => {
  it('is null unless the last outcome is failed', () => {
    expect(errorWords(status({ lastOutcome: 'ok' }))).toBeNull();
    expect(errorWords(status({ lastOutcome: 'never' }))).toBeNull();
    expect(errorWords(status({ lastOutcome: 'not-configured' }))).toBeNull();
  });
});

describe('canRunNow', () => {
  it('is false while an attempt is running', () => {
    expect(canRunNow(status(), true)).toBe(false);
  });

  it('is false when no folder is configured', () => {
    expect(canRunNow(status({ configured: false }), false)).toBe(false);
  });

  it('is true otherwise', () => {
    expect(canRunNow(status(), false)).toBe(true);
  });
});
