import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  REMEMBERED_PROJECT_STORAGE_KEY,
  readRememberedProject,
  resolveDefaultProject,
  writeRememberedProject,
} from './defaultProject';
import type { Project } from '../types';

/** A localStorage that holds what it is given, and can be made to throw - the
    same stub phoneBoard.test.ts uses for its storage pair. */
function stubStorage(initial: Record<string, string> = {}, throws = false) {
  const data = { ...initial };
  const storage = {
    getItem: (k: string) => {
      if (throws) throw new Error('blocked');
      return k in data ? data[k] : null;
    },
    setItem: (k: string, v: string) => {
      if (throws) throw new Error('blocked');
      data[k] = v;
    },
  };
  vi.stubGlobal('window', { localStorage: storage });
  return data;
}

afterEach(() => vi.unstubAllGlobals());

const project = (over: Partial<Project>): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  ...over,
});

describe('readRememberedProject / writeRememberedProject', () => {
  it('round-trips a key', () => {
    stubStorage();
    writeRememberedProject('AER');
    expect(readRememberedProject()).toBe('AER');
  });

  it('is null when nothing has been remembered', () => {
    stubStorage();
    expect(readRememberedProject()).toBeNull();
  });

  it('reads back what was written under the storage key directly', () => {
    stubStorage({ [REMEMBERED_PROJECT_STORAGE_KEY]: 'HA' });
    expect(readRememberedProject()).toBe('HA');
  });

  it('is null when storage throws', () => {
    stubStorage({}, true);
    expect(readRememberedProject()).toBeNull();
  });

  it('does not throw when storage cannot be written', () => {
    stubStorage({}, true);
    expect(() => writeRememberedProject('AER')).not.toThrow();
  });
});

describe('resolveDefaultProject', () => {
  const projects = [project({ id: 1, key: 'AER' }), project({ id: 2, key: 'HA' })];

  it('the filter wins over the remembered project', () => {
    expect(resolveDefaultProject(projects, 'HA', 'AER')).toBe(2);
  });

  it('falls through to the remembered project when the filter names none', () => {
    expect(resolveDefaultProject(projects, 'NOPE', 'HA')).toBe(2);
  });

  it('falls through to the first project when both name none', () => {
    expect(resolveDefaultProject(projects, 'NOPE', 'NOPE')).toBe(1);
  });

  it('falls through to the first project when there is no remembered key', () => {
    expect(resolveDefaultProject(projects, 'NOPE', null)).toBe(1);
  });

  it('is null when there are no projects', () => {
    expect(resolveDefaultProject([], 'AER', 'AER')).toBeNull();
  });
});
