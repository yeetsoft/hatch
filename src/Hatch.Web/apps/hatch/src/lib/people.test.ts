import { describe, expect, it } from 'vitest';
import type { Person, PersonRole } from '../types';
import { canChangeRole, canDelete, deleteSentence, demotionSentence, isDemotion, loneAdmin, sortPeople } from './people';

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
