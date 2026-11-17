import { describe, expect, it, vi } from 'vitest';
import type { Person, PersonRole, PersonScope, PersonScopeMode } from '../types';
import {
  addUserWithScope,
  canAddUser,
  canChangeRole,
  canDelete,
  deleteSentence,
  demotionSentence,
  isDemotion,
  isNarrowing,
  loneAdmin,
  narrowingSentence,
  scopeForMode,
  scopeLabel,
  sortPeople,
} from './people';

const person = (name: string, role: PersonRole, id = name): Person => ({
  id,
  name,
  role,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  photoUpdatedAt: null,
  sessionCount: 0,
  email: null,
  provider: null,
  lastSignInAt: null,
});

describe('which rows may change', () => {
  it('refuses the lone Admin', () => {
    const people = [person('Ada', 'admin'), person('Grace', 'user')];
    expect(loneAdmin(people)?.name).toBe('Ada');
    expect(canChangeRole(people[0], people)).toBe(false);
    expect(canDelete(people[0], people)).toBe(false);
    expect(canChangeRole(people[1], people)).toBe(true);
  });

  it('allows either of two Admins', () => {
    const people = [person('Ada', 'admin'), person('Grace', 'admin')];
    expect(loneAdmin(people)).toBeNull();
    expect(people.every((p) => canChangeRole(p, people))).toBe(true);
  });
});

describe('demotions', () => {
  it('is a demotion only when the role goes down', () => {
    expect(isDemotion('admin', 'user')).toBe(true);
    expect(isDemotion('user', 'pending')).toBe(true);
    expect(isDemotion('pending', 'user')).toBe(false);
    expect(isDemotion('user', 'user')).toBe(false);
  });

  it('says what is lost', () => {
    expect(demotionSentence('Ada', 'admin', 'user')).toContain('lose people');
    expect(demotionSentence('Ada', 'user', 'pending')).toContain('reach nothing');
  });
});

describe('deleting', () => {
  it('says the sessions end with the person', () => {
    expect(deleteSentence({ name: 'Ada', sessionCount: 1 })).toBe('Ada will be removed. Their session ends with them.');
    expect(deleteSentence({ name: 'Ada', sessionCount: 3 })).toContain('3 sessions end with them');
    expect(deleteSentence({ name: 'Ada', sessionCount: 0 })).toContain('no sessions');
  });
});

describe('sorting', () => {
  it('sorts by name regardless of case, ties by id, without mutating', () => {
    const input = [person('bea', 'user', '2'), person('Ada', 'user', '9'), person('Ada', 'user', '1')];
    expect(sortPeople(input).map((p) => p.id)).toEqual(['1', '9', '2']);
    expect(input[0].id).toBe('2');
  });
});

describe('the Add user form', () => {
  it('needs an address, and nothing else', () => {
    expect(canAddUser('')).toBe(false);
    expect(canAddUser('   ')).toBe(false);
    expect(canAddUser('someone@example.com')).toBe(true);
  });
});

const EVERY = [1, 2, 3];
const scope = (mode: PersonScopeMode, projectIds: number[] = []): PersonScope => ({ personId: 'p', mode, projectIds });
const ALL = scope('all', EVERY);
const NONE = scope('none');

describe('scope labels', () => {
  it('counts the ticked projects under some', () => {
    expect(scopeLabel(scope('some', []))).toBe('some (0)');
    expect(scopeLabel(scope('some', [1, 2]))).toBe('some (2)');
    expect(scopeLabel(ALL)).toBe('all');
    expect(scopeLabel(NONE)).toBe('none');
  });
});

describe('narrowing a scope', () => {
  it('is narrowing only when a project is dropped', () => {
    expect(isNarrowing(ALL, NONE)).toBe(true);
    expect(isNarrowing(ALL, scope('some', [1]))).toBe(true);
    expect(isNarrowing(ALL, scope('some', EVERY))).toBe(false);
    expect(isNarrowing(ALL, ALL)).toBe(false);
    expect(isNarrowing(NONE, ALL)).toBe(false);
    expect(isNarrowing(NONE, scope('some', [1]))).toBe(false);
    expect(isNarrowing(NONE, NONE)).toBe(false);
    expect(isNarrowing(scope('some', [1]), ALL)).toBe(false);
    expect(isNarrowing(scope('some', [1]), NONE)).toBe(true);
    expect(isNarrowing(scope('some', []), NONE)).toBe(false);
    expect(isNarrowing(scope('some', [1]), scope('some', [1]))).toBe(false);
  });

  it('judges some to some by what is dropped, not what is added', () => {
    expect(isNarrowing(scope('some', [1, 2]), scope('some', [2, 3]))).toBe(true);
    expect(isNarrowing(scope('some', [1, 2]), scope('some', [1, 2, 3]))).toBe(false);
  });

  it('says how many projects go', () => {
    expect(narrowingSentence('Ada', ALL, NONE)).toBe('Ada goes from all to none: they will stop seeing 3 projects.');
    expect(narrowingSentence('Ada', scope('some', [1, 2]), scope('some', [2]))).toBe(
      'Ada goes from some (2) to some (1): they will stop seeing 1 project.',
    );
  });
});

describe('choosing a mode', () => {
  it('carries the held projects over to some, and sends none otherwise', () => {
    expect(scopeForMode(ALL, 'some')).toEqual({ mode: 'some', projectIds: EVERY });
    expect(scopeForMode(NONE, 'some')).toEqual({ mode: 'some', projectIds: [] });
    expect(scopeForMode(scope('some', [2]), 'all')).toEqual({ mode: 'all' });
    expect(scopeForMode(ALL, 'none')).toEqual({ mode: 'none' });
  });

  it('does not share the held list with the request', () => {
    const held = scope('some', [1]);
    const request = scopeForMode(held, 'some');
    request.projectIds?.push(9);
    expect(held.projectIds).toEqual([1]);
  });
});

describe('adding a user with a scope', () => {
  const request = { email: 'ada@example.com', role: 'user' as PersonRole };

  it('makes no second call for all', async () => {
    const create = vi.fn().mockResolvedValue({ id: 'new' });
    const setScope = vi.fn();
    await addUserWithScope(create, setScope, request, { mode: 'all' });
    expect(create).toHaveBeenCalledWith(request);
    expect(setScope).not.toHaveBeenCalled();
  });

  it('saves none and some against the new id', async () => {
    const create = vi.fn().mockResolvedValue({ id: 'new' });
    const setScope = vi.fn().mockResolvedValue(undefined);
    await addUserWithScope(create, setScope, request, { mode: 'none' });
    await addUserWithScope(create, setScope, request, { mode: 'some', projectIds: [2] });
    expect(setScope).toHaveBeenNthCalledWith(1, 'new', { mode: 'none' });
    expect(setScope).toHaveBeenNthCalledWith(2, 'new', { mode: 'some', projectIds: [2] });
  });

  it('says the person was added on all when the scope is refused', async () => {
    const create = vi.fn().mockResolvedValue({ id: 'new' });
    const setScope = vi.fn().mockRejectedValue(new Error('Admins only.'));
    await expect(addUserWithScope(create, setScope, request, { mode: 'none' })).rejects.toThrow(
      'ada@example.com was added with scope all, and the scope was not saved: Admins only.',
    );
  });

  it('never reaches the scope when the person is refused', async () => {
    const create = vi.fn().mockRejectedValue(new Error('Already there.'));
    const setScope = vi.fn();
    await expect(addUserWithScope(create, setScope, request, { mode: 'none' })).rejects.toThrow('Already there.');
    expect(setScope).not.toHaveBeenCalled();
  });
});
