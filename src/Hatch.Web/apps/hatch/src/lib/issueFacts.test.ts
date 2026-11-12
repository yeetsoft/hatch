import { describe, expect, it } from 'vitest';
import { assigneeFact, effortFact, modelFact, wipLimitFact } from './issueFacts';
import type { Assignee } from '../types';

const person = (name: string, id = `p-${name}`): Assignee => ({ kind: 'person', id, name });
const key = (name: string, id = `k-${name}`): Assignee => ({ kind: 'key', id, name });

describe('assigneeFact', () => {
  it('reads nobody as an em dash', () => {
    expect(assigneeFact(null)).toBe('—');
  });

  it("names a person's kind alongside their name", () => {
    expect(assigneeFact(person('Pam Beesly'))).toBe('Pam Beesly (person)');
  });

  it("names a key's kind alongside its name", () => {
    expect(assigneeFact(key('Codex'))).toBe('Codex (key)');
  });
});

describe('modelFact', () => {
  it('returns a set override verbatim, including a hand-typed pin', () => {
    expect(modelFact('claude-opus-5')).toBe('claude-opus-5');
  });

  it('says the playbook decides when unset', () => {
    expect(modelFact(null)).toBe('the playbook decides');
  });
});

describe('effortFact', () => {
  it('returns a set override verbatim', () => {
    expect(effortFact('high')).toBe('high');
  });

  it('says the playbook decides when unset', () => {
    expect(effortFact(null)).toBe('the playbook decides');
  });
});

describe('wipLimitFact', () => {
  it('returns a set limit as a plain number', () => {
    expect(wipLimitFact(3)).toBe('3');
  });

  it('says no limit when unset', () => {
    expect(wipLimitFact(null)).toBe('no limit');
  });
});
