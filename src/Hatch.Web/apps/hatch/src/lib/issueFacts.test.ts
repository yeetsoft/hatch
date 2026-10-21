import { describe, expect, it } from 'vitest';
import { assigneeFact, effortFact, modelFact, preApprovalFact, priorityFact, wipLimitFact } from './issueFacts';
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

describe('priorityFact', () => {
  it("reads an issue's own level as the plain word", () => {
    expect(priorityFact('expedited', null)).toBe('Expedited');
  });

  it('says where an inherited level came from', () => {
    expect(priorityFact('expedited', 'AER-1')).toBe('Expedited · inherited from AER-1');
  });

  it('capitalizes economy correctly', () => {
    expect(priorityFact('economy', null)).toBe('Economy');
  });
});

describe('preApprovalFact', () => {
  it('reads none as the plain word', () => {
    expect(preApprovalFact('none', null)).toBe('None');
  });

  it('reads express as the plain word', () => {
    expect(preApprovalFact('express', null)).toBe('Express');
  });

  it('reads preapproved as the plain word', () => {
    expect(preApprovalFact('preapproved', null)).toBe('Pre-approved');
  });

  it('says where an inherited level came from', () => {
    expect(preApprovalFact('preapproved', 'AER-1')).toBe('Pre-approved · inherited from AER-1');
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
